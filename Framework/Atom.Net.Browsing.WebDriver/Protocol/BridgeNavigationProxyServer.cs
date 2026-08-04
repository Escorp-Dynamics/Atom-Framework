using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeNavigationProxyDirectRequest(
    string Method,
    string Path,
    string? Secret,
    string BodyText);

internal sealed record BridgeNavigationProxyDirectResponse(
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string>? Headers,
    byte[]? Body);

[SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Server certificate lifetime is owned by the shared certificate manager.")]
internal sealed class BridgeNavigationProxyServer(
    string host,
    int port,
    Func<ProxyNavigationDecisionRegistry?> registryResolver,
    Func<BridgeNavigationProxyDirectRequest, CancellationToken, ValueTask<BridgeNavigationProxyDirectResponse?>>? directRequestHandler = null,
    ILogger? diagnosticsLogger = null) : IAsyncDisposable
{
    private const string ProxyAuthenticationRealm = "Basic realm=\"Atom Bridge Navigation Proxy\"";
    private const int MaxRequestHeaderBytes = 128 * 1024;
    private const int MaxRequestBodyBytes = 32 * 1024 * 1024;
    private const int MaxForwardResponseBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan ConnectionIoTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(60);

    private static readonly IReadOnlySet<string> HopByHopHeaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Proxy-Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    private readonly TcpListener listener = new(ResolveBindableAddress(host), port);
    private readonly CancellationTokenSource cts = new();
    private readonly Func<ProxyNavigationDecisionRegistry?> registryResolver = registryResolver;
    private readonly Func<BridgeNavigationProxyDirectRequest, CancellationToken, ValueTask<BridgeNavigationProxyDirectResponse?>>? directRequestHandler = directRequestHandler;
    private readonly ILogger? logger = diagnosticsLogger;
    private readonly ConcurrentDictionary<string, HttpClient> forwardClients = new(StringComparer.Ordinal);
    private Task? acceptLoop;
    private bool isDisposed;

    public int Port { get; private set; } = port;

    internal ValueTask StartAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);
        logger?.LogBridgeServerNavigationProxyStarted(host, Port);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        await cts.CancelAsync().ConfigureAwait(false);

        try
        {
            listener.Stop();
        }
        catch (SocketException)
        {
            // Listener already stopped.
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (TimeoutException)
            {
                // Best-effort teardown.
            }
        }

        listener.Dispose();
        cts.Dispose();

        foreach (var forwardClient in forwardClients.Values)
            forwardClient.Dispose();

        forwardClients.Clear();
        logger?.LogBridgeServerNavigationProxyStopped(host, Port);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // Общий бюджет на ввод-вывод: зависший клиент не должен удерживать обработчик вечно.
        using var ioTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ioTimeout.CancelAfter(ConnectionIoTimeout);

        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var request = await ReadProxyRequestAsync(stream, ioTimeout.Token).ConfigureAwait(false);
                if (request is null)
                    return;

                if (await TryHandleDirectRequestAsync(stream, request, ioTimeout.Token).ConfigureAwait(false))
                    return;

                var routeToken = TryReadRouteToken(request.Headers);
                if (string.IsNullOrWhiteSpace(routeToken))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "proxy-auth-missing");
                    await WriteProxyAuthenticationRequiredAsync(stream, ioTimeout.Token).ConfigureAwait(false);
                    return;
                }

                var registry = registryResolver();
                if (registry is null || !registry.TryResolveRoute(routeToken, out var route))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "proxy-route-missing");
                    await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "route-missing", ioTimeout.Token).ConfigureAwait(false);
                    return;
                }

                if (request.IsConnect)
                {
                    await HandleConnectTunnelAsync(stream, request, routeToken, route, registry, ioTimeout.Token).ConfigureAwait(false);
                    return;
                }

                if (!TryBuildAbsoluteTargetUrl("http", request.Target, request.Headers, fallbackHost: null, fallbackPort: 0, out var absoluteTargetUrl))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "absolute-url-invalid");
                    await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-target", ioTimeout.Token).ConfigureAwait(false);
                    return;
                }

                await HandleNavigationRequestAsync(stream, request, absoluteTargetUrl, routeToken, route, registry, ioTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Остановка сервера или истекший бюджет ввода-вывода.
            }
            catch (IOException)
            {
                // Client disconnected before the response completed.
            }
            catch (AuthenticationException)
            {
                // TLS negotiation failed inside a CONNECT tunnel.
            }
            catch (Exception exception)
            {
                logger?.LogBridgeServerNavigationProxyConnectionFailed(exception);
            }
        }
    }

    private async Task<bool> TryHandleDirectRequestAsync(Stream stream, ProxyRequest request, CancellationToken cancellationToken)
    {
        if (request.IsConnect
            || directRequestHandler is null
            || !TryCreateDirectRequest(request, out var directRequest))
        {
            return false;
        }

        BridgeNavigationProxyDirectResponse? directResponse;
        try
        {
            directResponse = await directRequestHandler(directRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, string.Concat("direct-handler-failed:", exception.GetType().Name));
            await WriteErrorResponseAsync(
                stream,
                HttpStatusCode.BadGateway,
                "direct-request-handler-failed",
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (directResponse is null)
            return false;

        await WriteDirectResponseAsync(stream, directResponse, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task HandleConnectTunnelAsync(
        System.Net.Sockets.NetworkStream stream,
        ProxyRequest request,
        string routeToken,
        ProxyNavigationRoute route,
        ProxyNavigationDecisionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectHost) || request.ConnectPort <= 0)
        {
            logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "connect-target-invalid");
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-connect-target", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteConnectEstablishedAsync(stream, cancellationToken).ConfigureAwait(false);

        using var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);
        var certificate = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(request.ConnectHost);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        }, cancellationToken).ConfigureAwait(false);

        var tunneledRequest = await ReadProxyRequestAsync(sslStream, cancellationToken).ConfigureAwait(false);
        if (tunneledRequest is null)
            return;

        if (!TryBuildAbsoluteTargetUrl("https", tunneledRequest.Target, tunneledRequest.Headers, request.ConnectHost, request.ConnectPort, out var absoluteTargetUrl))
        {
            logger?.LogBridgeServerNavigationProxyRejected(tunneledRequest.Method, tunneledRequest.Target, "tunnel-target-invalid");
            await WriteErrorResponseAsync(sslStream, HttpStatusCode.BadRequest, "invalid-tunnel-target", cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandleNavigationRequestAsync(sslStream, tunneledRequest, absoluteTargetUrl, routeToken, route, registry, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleNavigationRequestAsync(
        Stream stream,
        ProxyRequest clientRequest,
        string absoluteTargetUrl,
        string routeToken,
        ProxyNavigationRoute route,
        ProxyNavigationDecisionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!TryConsumeDecision(registry, routeToken, clientRequest.Method, absoluteTargetUrl, DateTimeOffset.UtcNow, out var decision))
        {
            // Решения нет: запрос не проходил через мост (неперехваченная навигация, повтор после
            // истечения TTL и т.п.). Route token валиден, поэтому вместо 502 (который ломал бы
            // страницу целиком) работаем как обычный прокси и прозрачно форвардим запрос.
            logger?.LogBridgeServerNavigationProxyMatched("Continue(implicit)", clientRequest.Method, absoluteTargetUrl);
            await ForwardContinueDecisionAsync(
                stream,
                clientRequest,
                absoluteTargetUrl,
                route,
                CreateImplicitContinueDecision(clientRequest, absoluteTargetUrl),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        logger?.LogBridgeServerNavigationProxyMatched(decision.Action.ToString(), clientRequest.Method, absoluteTargetUrl);

        if (decision.Action is ProxyNavigationDecisionAction.Continue)
        {
            await ForwardContinueDecisionAsync(stream, clientRequest, absoluteTargetUrl, route, decision, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteMatchedDecisionResponseAsync(stream, clientRequest.Method, absoluteTargetUrl, decision, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMatchedDecisionResponseAsync(
        Stream stream,
        string method,
        string absoluteTargetUrl,
        ProxyNavigationPendingDecision decision,
        CancellationToken cancellationToken)
    {
        switch (decision.Action)
        {
            case ProxyNavigationDecisionAction.Fulfill:
                await WriteDecisionResponseAsync(
                    stream,
                    method,
                    ResolveStatusCode(decision.StatusCode, (int)HttpStatusCode.OK),
                    decision.ReasonPhrase,
                    decision.ResponseHeaders,
                    decision.ResponseBody,
                    location: null,
                    cancellationToken).ConfigureAwait(false);
                return;

            case ProxyNavigationDecisionAction.Redirect:
                await WriteRedirectDecisionResponseAsync(stream, method, absoluteTargetUrl, decision, cancellationToken).ConfigureAwait(false);
                return;

            case ProxyNavigationDecisionAction.Abort:
                await WriteDecisionResponseAsync(
                    stream,
                    method,
                    ResolveStatusCode(decision.StatusCode, (int)HttpStatusCode.Forbidden),
                    decision.ReasonPhrase,
                    decision.ResponseHeaders,
                    decision.ResponseBody,
                    location: null,
                    cancellationToken).ConfigureAwait(false);
                return;

            default:
                logger?.LogBridgeServerNavigationProxyRejected(method, absoluteTargetUrl, "decision-action-unsupported");
                await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "decision-action-unsupported", cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task WriteRedirectDecisionResponseAsync(
        Stream stream,
        string method,
        string absoluteTargetUrl,
        ProxyNavigationPendingDecision decision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decision.RedirectUrl))
        {
            logger?.LogBridgeServerNavigationProxyRejected(method, absoluteTargetUrl, "redirect-url-missing");
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "redirect-url-missing", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteDecisionResponseAsync(
            stream,
            method,
            ResolveRedirectStatusCode(decision.StatusCode, method),
            decision.ReasonPhrase,
            decision.ResponseHeaders,
            decision.ResponseBody,
            decision.RedirectUrl,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProxyRequest?> ReadProxyRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var requestEnvelope = await ReadRequestEnvelopeAsync(stream, cancellationToken).ConfigureAwait(false);
        if (requestEnvelope is null)
            return null;

        var (headerBytes, bufferedBodyBytes) = requestEnvelope.Value;
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var headerLines = headerText.Split("\r\n", StringSplitOptions.None);
        if (headerLines.Length == 0 || string.IsNullOrWhiteSpace(headerLines[0]))
            return null;

        var parts = headerLines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        var headers = ParseHeaders(headerLines);

        var method = parts[0];
        var target = parts[1];
        byte[] bodyBytes;
        if (IsChunkedTransferEncoding(headers))
        {
            bodyBytes = await ReadChunkedBodyBytesAsync(stream, bufferedBodyBytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var contentLength = TryReadContentLength(headers);
            bodyBytes = contentLength > 0 && contentLength <= MaxRequestBodyBytes
                ? await ReadRequestBodyBytesAsync(stream, bufferedBodyBytes, contentLength, cancellationToken).ConfigureAwait(false)
                : [];
        }

        return CreateProxyRequest(method, target, headers, bodyBytes);
    }

    private static bool IsChunkedTransferEncoding(IReadOnlyDictionary<string, string> headers)
        => headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static token => string.Equals(token, "chunked", StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> ReadChunkedBodyBytesAsync(Stream stream, byte[] bufferedBodyBytes, CancellationToken cancellationToken)
    {
        var cursor = new ChunkCursor(stream, bufferedBodyBytes);
        var body = new MemoryStream();

        while (true)
        {
            var sizeLine = await cursor.ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false);
            if (sizeLine is null)
                return body.ToArray();

            var sizeText = sizeLine.Split(';', 2)[0].Trim();
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                return body.ToArray();

            if (chunkSize == 0)
            {
                // Трейлер до терминального CRLF.
                while (await cursor.ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false) is { } trailerLine)
                {
                    if (trailerLine.Length == 0)
                        break;
                }

                return body.ToArray();
            }

            if (body.Length + chunkSize > MaxRequestBodyBytes)
                return body.ToArray();

            if (!await cursor.CopyExactlyAsync(body, chunkSize, cancellationToken).ConfigureAwait(false))
                return body.ToArray();

            // CRLF после данных чанка.
            _ = await cursor.ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Последовательный курсор чтения: сначала отдаёт байты, уже буферизированные разбором
    /// конверта заголовков, затем дочитывает из сетевого потока.
    /// </summary>
    private sealed class ChunkCursor(Stream stream, byte[] buffered)
    {
        private readonly byte[] buffered = buffered;
        private readonly byte[] scratch = new byte[8192];
        private int bufferedOffset;

        public async Task<string?> ReadAsciiLineAsync(CancellationToken cancellationToken)
        {
            var builder = new StringBuilder(128);
            while (true)
            {
                var value = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (value < 0)
                    return builder.Length == 0 ? null : builder.ToString();

                if (value == '\n')
                {
                    if (builder.Length > 0 && builder[^1] == '\r')
                        builder.Length -= 1;

                    return builder.ToString();
                }

                builder.Append((char)value);
                if (builder.Length > 64 * 1024)
                    return builder.ToString();
            }
        }

        public async Task<bool> CopyExactlyAsync(Stream destination, int count, CancellationToken cancellationToken)
        {
            var remaining = count;
            while (remaining > 0)
            {
                var read = await ReadAsync(scratch.AsMemory(0, Math.Min(scratch.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    return false;

                await destination.WriteAsync(scratch.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }

            return true;
        }

        private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (bufferedOffset < buffered.Length)
                return buffered[bufferedOffset++];

            var read = await stream.ReadAsync(scratch.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            return read <= 0 ? -1 : scratch[0];
        }

        private async Task<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (bufferedOffset < buffered.Length)
            {
                var available = Math.Min(buffered.Length - bufferedOffset, destination.Length);
                buffered.AsMemory(bufferedOffset, available).CopyTo(destination);
                bufferedOffset += available;
                return available;
            }

            return await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> ParseHeaders(string[] headerLines)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (var lineIndex = 1; lineIndex < headerLines.Length; lineIndex++)
        {
            var headerLine = headerLines[lineIndex];
            if (string.IsNullOrEmpty(headerLine))
                break;

            var separatorIndex = headerLine.IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            var name = headerLine[..separatorIndex].Trim();
            if (name.Length == 0)
                continue;

            headers[name] = headerLine[(separatorIndex + 1)..].Trim();
        }

        return headers;
    }

    private static ProxyRequest CreateProxyRequest(string method, string target, Dictionary<string, string> headers, byte[] bodyBytes)
    {
        if (string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            var connectUri = Uri.TryCreate(string.Concat("https://", target), UriKind.Absolute, out var parsedConnectUri)
                ? parsedConnectUri
                : null;

            return new ProxyRequest(
                Method: method,
                Target: target,
                Headers: headers,
                ConnectHost: connectUri?.Host,
                ConnectPort: connectUri?.Port ?? 0,
                Body: bodyBytes);
        }

        return new ProxyRequest(
            Method: method,
            Target: target,
            Headers: headers,
            ConnectHost: null,
            ConnectPort: 0,
            Body: bodyBytes);
    }

    private static async Task<(byte[] HeaderBytes, byte[] BufferedBodyBytes)?> ReadRequestEnvelopeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var chunk = new byte[1024];

        while (buffer.Count <= MaxRequestHeaderBytes)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;

            var previousCount = buffer.Count;
            for (var index = 0; index < read; index++)
            {
                buffer.Add(chunk[index]);
            }

            if (TryExtractRequestEnvelope(buffer, previousCount, out var envelope))
                return envelope;
        }

        // Заголовки сверх лимита — соединение закрывается без ответа.
        return null;
    }

    private static bool TryExtractRequestEnvelope(List<byte> buffer, int previousCount, out (byte[] HeaderBytes, byte[] BufferedBodyBytes) envelope)
    {
        var searchStart = Math.Max(0, previousCount - 3);
        for (var index = searchStart; index <= buffer.Count - 4; index++)
        {
            if (buffer[index] != '\r'
                || buffer[index + 1] != '\n'
                || buffer[index + 2] != '\r'
                || buffer[index + 3] != '\n')
            {
                continue;
            }

            envelope = (
                CopyBufferRange(buffer, 0, index),
                CopyBufferRange(buffer, index + 4, buffer.Count - (index + 4)));
            return true;
        }

        envelope = default;
        return false;
    }

    private static byte[] CopyBufferRange(List<byte> source, int offset, int length)
    {
        if (length <= 0)
            return [];

        var copy = new byte[length];
        for (var index = 0; index < length; index++)
        {
            copy[index] = source[offset + index];
        }

        return copy;
    }

    // Синтетическое решение «продолжить как есть» для запросов без зарегистрированного решения.
    private static ProxyNavigationPendingDecision CreateImplicitContinueDecision(ProxyRequest clientRequest, string absoluteTargetUrl)
    {
        var issuedAtUtc = DateTimeOffset.UtcNow;
        return new ProxyNavigationPendingDecision
        {
            RequestId = "implicit-continue",
            Method = clientRequest.Method,
            AbsoluteUrl = absoluteTargetUrl,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = issuedAtUtc,
            Action = ProxyNavigationDecisionAction.Continue,
        };
    }

    private async Task ForwardContinueDecisionAsync(
        Stream stream,
        ProxyRequest clientRequest,
        string absoluteTargetUrl,
        ProxyNavigationRoute route,
        ProxyNavigationPendingDecision decision,
        CancellationToken cancellationToken)
    {
        var forwardTargetUrl = string.IsNullOrWhiteSpace(decision.ForwardUrl)
            ? absoluteTargetUrl
            : decision.ForwardUrl;

        using var forwardTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        forwardTimeout.CancelAfter(ForwardTimeout);

        try
        {
            var client = GetForwardClient(route.UpstreamProxy);
            using var forwardRequest = CreateForwardRequest(clientRequest, decision, forwardTargetUrl);
            using var forwardResponse = await client.SendAsync(
                forwardRequest,
                HttpCompletionOption.ResponseHeadersRead,
                forwardTimeout.Token).ConfigureAwait(false);

            if (forwardResponse.Content.Headers.ContentLength > MaxForwardResponseBytes)
            {
                logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, absoluteTargetUrl, "forward-response-too-large");
                await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "forward-response-too-large", forwardTimeout.Token).ConfigureAwait(false);
                return;
            }

            var body = await ReadForwardResponseBodyAsync(forwardResponse, forwardTimeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, absoluteTargetUrl, "forward-response-too-large");
                await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "forward-response-too-large", forwardTimeout.Token).ConfigureAwait(false);
                return;
            }

            var responseHeaders = CollectForwardResponseHeaders(forwardResponse);
            await WriteResponseAsync(
                stream,
                (int)forwardResponse.StatusCode,
                forwardResponse.ReasonPhrase,
                responseHeaders,
                body,
                includeBody: !string.Equals(clientRequest.Method, "HEAD", StringComparison.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            logger?.LogBridgeServerNavigationProxyForwardFailed(clientRequest.Method, absoluteTargetUrl, exception);
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "decision-forward-failed", CancellationToken.None).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, absoluteTargetUrl, "forward-timeout");
            await WriteErrorResponseAsync(stream, HttpStatusCode.GatewayTimeout, "forward-timeout", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> ReadForwardResponseBodyAsync(HttpResponseMessage forwardResponse, CancellationToken cancellationToken)
    {
        if (forwardResponse.Content.Headers.ContentLength == 0)
            return [];

        if (forwardResponse.Content.Headers.ContentLength is { } knownLength && knownLength > MaxForwardResponseBytes)
            return null;

        var body = await forwardResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxForwardResponseBytes ? null : body;
    }

    private HttpRequestMessage CreateForwardRequest(
        ProxyRequest clientRequest,
        ProxyNavigationPendingDecision decision,
        string forwardTargetUrl)
    {
        var request = new HttpRequestMessage(new HttpMethod(clientRequest.Method), forwardTargetUrl);
        var body = decision.RequestBody is { Length: > 0 } ? decision.RequestBody : clientRequest.Body;
        if (body is { Length: > 0 })
            request.Content = new ByteArrayContent(body);

        var headerSource = decision.RequestHeaders ?? clientRequest.Headers;
        foreach (var (name, value) in headerSource)
        {
            if (string.IsNullOrWhiteSpace(name)
                || HopByHopHeaderNames.Contains(name)
                || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || !IsSafeHeaderValue(value))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        if (request.Content is not null && !request.Content.Headers.Contains("Content-Length"))
            request.Content.Headers.ContentLength = body!.Length;

        return request;
    }

    private static Dictionary<string, string> CollectForwardResponseHeaders(HttpResponseMessage forwardResponse)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in forwardResponse.Headers)
            AddForwardHeader(headers, name, values);

        if (forwardResponse.Content is not null)
        {
            foreach (var (name, values) in forwardResponse.Content.Headers)
                AddForwardHeader(headers, name, values);
        }

        return headers;
    }

    private static void AddForwardHeader(Dictionary<string, string> headers, string name, IEnumerable<string> values)
    {
        if (string.IsNullOrWhiteSpace(name)
            || HopByHopHeaderNames.Contains(name)
            || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var joined = string.Join(", ", values);
        if (IsSafeHeaderValue(joined))
            headers[name] = joined;
    }

    private static bool IsSafeHeaderValue(string? value)
        => value is not null && !value.Contains('\r') && !value.Contains('\n');

    private HttpClient GetForwardClient(string? upstreamProxy)
    {
        var key = upstreamProxy ?? string.Empty;
        return forwardClients.GetOrAdd(key, static upstream => CreateForwardClient(upstream));
    }

    private static HttpClient CreateForwardClient(string upstreamProxySpec)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        };

        if (!string.IsNullOrWhiteSpace(upstreamProxySpec)
            && Uri.TryCreate(upstreamProxySpec, UriKind.Absolute, out var upstreamProxyUri))
        {
            var proxy = new WebProxy(upstreamProxyUri.GetLeftPart(UriPartial.Authority));
            if (!string.IsNullOrEmpty(upstreamProxyUri.UserInfo))
            {
                var separatorIndex = upstreamProxyUri.UserInfo.IndexOf(':');
                proxy.Credentials = separatorIndex >= 0
                    ? new NetworkCredential(upstreamProxyUri.UserInfo[..separatorIndex], upstreamProxyUri.UserInfo[(separatorIndex + 1)..])
                    : new NetworkCredential(upstreamProxyUri.UserInfo, string.Empty);
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        return new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    private static int TryReadContentLength(Dictionary<string, string> headers)
        => headers.TryGetValue("Content-Length", out var rawContentLength)
            && int.TryParse(rawContentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contentLength)
            && contentLength > 0
                ? contentLength
                : 0;

    private static async Task<byte[]> ReadRequestBodyBytesAsync(Stream stream, byte[] bufferedBodyBytes, int contentLength, CancellationToken cancellationToken)
    {
        if (bufferedBodyBytes.Length >= contentLength)
            return bufferedBodyBytes[..contentLength];

        var result = new byte[contentLength];
        if (bufferedBodyBytes.Length > 0)
        {
            Buffer.BlockCopy(bufferedBodyBytes, 0, result, 0, bufferedBodyBytes.Length);
        }

        var offset = bufferedBodyBytes.Length;
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset, contentLength - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            offset += read;
        }

        return offset == contentLength
            ? result
            : result[..offset];
    }

    private static bool TryCreateDirectRequest(ProxyRequest request, [NotNullWhen(true)] out BridgeNavigationProxyDirectRequest? directRequest)
    {
        directRequest = null;

        if (string.IsNullOrWhiteSpace(request.Target)
            || !request.Target.StartsWith('/'))
        {
            return false;
        }

        if (!Uri.TryCreate(string.Concat("http://127.0.0.1", request.Target), UriKind.Absolute, out var requestUri))
            return false;

        directRequest = new(
            request.Method,
            requestUri.AbsolutePath,
            TryReadQueryParameter(requestUri.Query, "secret"),
            request.Body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(request.Body));
        return true;
    }

    private static string? TryReadQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            var name = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
            if (!string.Equals(WebUtility.UrlDecode(name), parameterName, StringComparison.Ordinal))
                continue;

            var rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;
            return WebUtility.UrlDecode(rawValue);
        }

        return null;
    }

    private static Task WriteDirectResponseAsync(Stream stream, BridgeNavigationProxyDirectResponse response, CancellationToken cancellationToken)
        => WriteResponseAsync(
            stream,
            response.StatusCode,
            response.ReasonPhrase,
            response.Headers,
            response.Body,
            includeBody: response.Body is { Length: > 0 },
            cancellationToken);

    private static bool TryBuildAbsoluteTargetUrl(
        string scheme,
        string rawTarget,
        IReadOnlyDictionary<string, string> headers,
        string? fallbackHost,
        int fallbackPort,
        [NotNullWhen(true)] out string? absoluteTargetUrl)
    {
        if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var absoluteUri)
            && (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            absoluteTargetUrl = absoluteUri.AbsoluteUri;
            return true;
        }

        var authority = ResolveAuthority(headers, fallbackHost, fallbackPort);
        if (string.IsNullOrWhiteSpace(authority)
            || !Uri.TryCreate(string.Concat(scheme, "://", authority), UriKind.Absolute, out var baseUri))
        {
            absoluteTargetUrl = null;
            return false;
        }

        var relativeTarget = NormalizeRelativeTarget(rawTarget);

        if (!Uri.TryCreate(baseUri, relativeTarget, out var resolvedUri))
        {
            absoluteTargetUrl = null;
            return false;
        }

        absoluteTargetUrl = resolvedUri.AbsoluteUri;
        return true;
    }

    private static string? ResolveAuthority(
        IReadOnlyDictionary<string, string> headers,
        string? fallbackHost,
        int fallbackPort)
    {
        if (headers.TryGetValue("Host", out var hostHeader)
            && !string.IsNullOrWhiteSpace(hostHeader))
        {
            return hostHeader;
        }

        if (string.IsNullOrWhiteSpace(fallbackHost))
            return null;

        return fallbackPort > 0
            ? string.Concat(fallbackHost, ":", fallbackPort.ToString(CultureInfo.InvariantCulture))
            : fallbackHost;
    }

    private static string NormalizeRelativeTarget(string rawTarget)
    {
        if (string.IsNullOrWhiteSpace(rawTarget))
            return "/";

        return rawTarget.StartsWith('/')
            ? rawTarget
            : string.Concat("/", rawTarget);
    }

    private static string? TryReadRouteToken(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Proxy-Authorization", out var proxyAuthorization)
            || string.IsNullOrWhiteSpace(proxyAuthorization))
        {
            return null;
        }

        const string prefix = "Basic ";
        if (!proxyAuthorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(proxyAuthorization[prefix.Length..].Trim()));
            var separatorIndex = credentials.IndexOf(':');
            var username = separatorIndex >= 0 ? credentials[..separatorIndex] : credentials;
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static async Task WriteProxyAuthenticationRequiredAsync(Stream stream, CancellationToken cancellationToken)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Proxy-Authenticate"] = ProxyAuthenticationRealm,
        };

        await WriteResponseAsync(
            stream,
            statusCode: (int)HttpStatusCode.ProxyAuthenticationRequired,
            reasonPhrase: null,
            headers,
            body: null,
            includeBody: false,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteConnectEstablishedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 Connection Established\r\nProxy-Agent: Atom Bridge Navigation Proxy\r\n\r\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteErrorResponseAsync(Stream stream, HttpStatusCode statusCode, string reason, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(reason);
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = "text/plain; charset=utf-8",
        };

        return WriteResponseAsync(stream, (int)statusCode, reasonPhrase: null, headers, body, includeBody: true, cancellationToken);
    }

    private static Task WriteDecisionResponseAsync(
        Stream stream,
        string method,
        int statusCode,
        string? reasonPhrase,
        IReadOnlyDictionary<string, string>? responseHeaders,
        byte[]? responseBody,
        string? location,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (responseHeaders is not null)
        {
            foreach (var (key, value) in responseHeaders)
            {
                if (string.IsNullOrWhiteSpace(key)
                    || !IsSafeHeaderValue(key)
                    || !IsSafeHeaderValue(value)
                    || HopByHopHeaderNames.Contains(key)
                    || string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                headers[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(location))
            headers["Location"] = location;

        return WriteResponseAsync(
            stream,
            statusCode,
            reasonPhrase,
            headers,
            responseBody,
            includeBody: !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int statusCode,
        string? reasonPhrase,
        IReadOnlyDictionary<string, string>? headers,
        byte[]? body,
        bool includeBody,
        CancellationToken cancellationToken)
    {
        var effectiveBody = body ?? [];
        var effectiveReasonPhrase = string.IsNullOrWhiteSpace(reasonPhrase)
            ? GetReasonPhrase(statusCode)
            : reasonPhrase;
        var headerBytes = Encoding.ASCII.GetBytes(BuildResponseHeader(statusCode, effectiveReasonPhrase, headers, effectiveBody.Length));
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        if (includeBody && effectiveBody.Length > 0)
            await stream.WriteAsync(effectiveBody, cancellationToken).ConfigureAwait(false);

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryConsumeDecision(
        ProxyNavigationDecisionRegistry registry,
        string routeToken,
        string method,
        string absoluteTargetUrl,
        DateTimeOffset nowUtc,
        [NotNullWhen(true)] out ProxyNavigationPendingDecision? decision)
    {
        foreach (var candidateUrl in EnumerateDecisionLookupUrls(absoluteTargetUrl))
        {
            if (registry.TryConsumeDecision(routeToken, method, candidateUrl, nowUtc, out decision))
                return true;
        }

        decision = null;
        return false;
    }

    private static HashSet<string> EnumerateDecisionLookupUrls(string absoluteTargetUrl)
    {
        HashSet<string> candidates = [absoluteTargetUrl];

        if (Uri.TryCreate(absoluteTargetUrl, UriKind.Absolute, out var uri))
        {
            var defaultPort = uri.Scheme switch
            {
                "http" => 80,
                "https" => 443,
                _ => 0,
            };

            if (defaultPort > 0)
            {
                var withoutPort = new UriBuilder(uri)
                {
                    Port = -1,
                }.Uri.AbsoluteUri;
                candidates.Add(withoutPort);

                var withDefaultPort = new UriBuilder(uri)
                {
                    Port = defaultPort,
                }.Uri.AbsoluteUri;
                candidates.Add(withDefaultPort);
            }
        }

        return candidates;
    }

    private static string BuildResponseHeader(
        int statusCode,
        string reasonPhrase,
        IReadOnlyDictionary<string, string>? headers,
        int contentLength)
    {
        var headerBuilder = new ValueStringBuilder(512);

        try
        {
            headerBuilder.Append("HTTP/1.1 ");
            headerBuilder.Append(statusCode);
            headerBuilder.Append(' ');
            headerBuilder.Append(reasonPhrase);
            headerBuilder.Append("\r\n");

            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    headerBuilder.Append(key);
                    headerBuilder.Append(": ");
                    headerBuilder.Append(value);
                    headerBuilder.Append("\r\n");
                }
            }

            headerBuilder.Append("Content-Length: ");
            headerBuilder.Append(contentLength);
            headerBuilder.Append("\r\nConnection: close\r\n\r\n");
            return headerBuilder.ToString();
        }
        finally
        {
            headerBuilder.Dispose();
        }
    }

    private static string GetReasonPhrase(int statusCode)
        => statusCode switch
        {
            (int)HttpStatusCode.OK => "OK",
            (int)HttpStatusCode.Created => "Created",
            (int)HttpStatusCode.Accepted => "Accepted",
            (int)HttpStatusCode.NonAuthoritativeInformation => "Non-Authoritative Information",
            (int)HttpStatusCode.NoContent => "No Content",
            (int)HttpStatusCode.ResetContent => "Reset Content",
            (int)HttpStatusCode.PartialContent => "Partial Content",
            (int)HttpStatusCode.MultipleChoices => "Multiple Choices",
            (int)HttpStatusCode.MovedPermanently => "Moved Permanently",
            (int)HttpStatusCode.Found => "Found",
            (int)HttpStatusCode.SeeOther => "See Other",
            (int)HttpStatusCode.NotModified => "Not Modified",
            (int)HttpStatusCode.TemporaryRedirect => "Temporary Redirect",
            (int)HttpStatusCode.PermanentRedirect => "Permanent Redirect",
            (int)HttpStatusCode.BadRequest => "Bad Request",
            (int)HttpStatusCode.Unauthorized => "Unauthorized",
            (int)HttpStatusCode.Forbidden => "Forbidden",
            (int)HttpStatusCode.NotFound => "Not Found",
            (int)HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            (int)HttpStatusCode.RequestTimeout => "Request Timeout",
            (int)HttpStatusCode.Gone => "Gone",
            (int)HttpStatusCode.RequestEntityTooLarge => "Request Entity Too Large",
            (int)HttpStatusCode.RequestUriTooLong => "Request-URI Too Long",
            (int)HttpStatusCode.UnsupportedMediaType => "Unsupported Media Type",
            (int)HttpStatusCode.ProxyAuthenticationRequired => "Proxy Authentication Required",
            (int)HttpStatusCode.TooManyRequests => "Too Many Requests",
            (int)HttpStatusCode.InternalServerError => "Internal Server Error",
            (int)HttpStatusCode.BadGateway => "Bad Gateway",
            (int)HttpStatusCode.ServiceUnavailable => "Service Unavailable",
            (int)HttpStatusCode.GatewayTimeout => "Gateway Timeout",
            _ => string.Concat("Status ", statusCode.ToString(CultureInfo.InvariantCulture)),
        };

    private static int ResolveStatusCode(int? statusCode, int defaultStatusCode)
        => statusCode ?? defaultStatusCode;

    private static int ResolveRedirectStatusCode(int? statusCode, string method)
        => statusCode ?? (IsSafeMethod(method) ? (int)HttpStatusCode.Found : (int)HttpStatusCode.TemporaryRedirect);

    private static bool IsSafeMethod(string method)
        => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    private static IPAddress ResolveBindableAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
            return address;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;

        throw new InvalidOperationException($"Navigation proxy endpoint не умеет привязываться к хосту '{host}'");
    }

    private sealed record ProxyRequest(
        string Method,
        string Target,
        IReadOnlyDictionary<string, string> Headers,
        string? ConnectHost,
        int ConnectPort,
        byte[] Body)
    {
        internal bool IsConnect => string.Equals(Method, "CONNECT", StringComparison.OrdinalIgnoreCase);
    }
}