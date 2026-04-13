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

    private readonly TcpListener listener = new(ResolveBindableAddress(host), port);
    private readonly CancellationTokenSource cts = new();
    private readonly Func<ProxyNavigationDecisionRegistry?> registryResolver = registryResolver;
    private readonly Func<BridgeNavigationProxyDirectRequest, CancellationToken, ValueTask<BridgeNavigationProxyDirectResponse?>>? directRequestHandler = directRequestHandler;
    private readonly ILogger? logger = diagnosticsLogger;
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
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var request = await ReadProxyRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request is null)
                    return;

                if (await TryHandleDirectRequestAsync(stream, request, cancellationToken).ConfigureAwait(false))
                    return;

                var routeToken = TryReadRouteToken(request.Headers);
                if (string.IsNullOrWhiteSpace(routeToken))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "proxy-auth-missing");
                    await WriteProxyAuthenticationRequiredAsync(stream, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var registry = registryResolver();
                if (registry is null || !registry.TryResolveRoute(routeToken, out _))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "proxy-route-missing");
                    await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "route-missing", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (request.IsConnect)
                {
                    await HandleConnectTunnelAsync(stream, request, routeToken, registry, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!TryBuildAbsoluteTargetUrl("http", request.Target, request.Headers, fallbackHost: null, fallbackPort: 0, out var absoluteTargetUrl))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "absolute-url-invalid");
                    await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-target", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await HandleNavigationRequestAsync(stream, request.Method, absoluteTargetUrl, routeToken, registry, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Server shutdown.
            }
            catch (IOException)
            {
                // Client disconnected before the response completed.
            }
            catch (AuthenticationException)
            {
                // TLS negotiation failed inside a CONNECT tunnel.
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

        await HandleNavigationRequestAsync(sslStream, tunneledRequest.Method, absoluteTargetUrl, routeToken, registry, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleNavigationRequestAsync(
        Stream stream,
        string method,
        string absoluteTargetUrl,
        string routeToken,
        ProxyNavigationDecisionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!TryConsumeDecision(registry, routeToken, method, absoluteTargetUrl, DateTimeOffset.UtcNow, out var decision))
        {
            logger?.LogBridgeServerNavigationProxyRejected(method, absoluteTargetUrl, "decision-missing");
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "decision-missing", cancellationToken).ConfigureAwait(false);
            return;
        }

        logger?.LogBridgeServerNavigationProxyMatched(decision.Action.ToString(), method, absoluteTargetUrl);

        await WriteMatchedDecisionResponseAsync(stream, method, absoluteTargetUrl, decision, cancellationToken).ConfigureAwait(false);
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
        var contentLength = TryReadContentLength(headers);
        var bodyBytes = contentLength > 0
            ? await ReadRequestBodyBytesAsync(stream, bufferedBodyBytes, contentLength, cancellationToken).ConfigureAwait(false)
            : [];

        return CreateProxyRequest(method, target, headers, bodyBytes);
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

        while (true)
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
                    || string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "Connection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
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
            (int)HttpStatusCode.NoContent => "No Content",
            (int)HttpStatusCode.Found => "Found",
            (int)HttpStatusCode.SeeOther => "See Other",
            (int)HttpStatusCode.TemporaryRedirect => "Temporary Redirect",
            (int)HttpStatusCode.Forbidden => "Forbidden",
            (int)HttpStatusCode.BadRequest => "Bad Request",
            (int)HttpStatusCode.NotFound => "Not Found",
            (int)HttpStatusCode.ProxyAuthenticationRequired => "Proxy Authentication Required",
            (int)HttpStatusCode.BadGateway => "Bad Gateway",
            _ => "OK",
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