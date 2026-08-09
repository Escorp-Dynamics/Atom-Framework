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
    ILogger? diagnosticsLogger = null,
    Func<ProxyNavigationRoute, string, string, string, string, IReadOnlyDictionary<string, string>?, CancellationToken, ValueTask>? interceptionDispatcher = null) : IAsyncDisposable
{
    private const string ProxyAuthenticationRealm = "Basic realm=\"Atom Bridge Navigation Proxy\"";
    private const int MaxRequestHeaderBytes = 128 * 1024;
    private const int MaxRequestBodyBytes = 32 * 1024 * 1024;
    private const int MaxForwardResponseBytes = 64 * 1024 * 1024;
    /// <summary>
    /// Бюджет одной фазы ввода-вывода с клиентом (чтение запроса, TLS-рукопожатие туннеля,
    /// запись ответа). Считается для каждой фазы отдельно: общий бюджет на всё соединение
    /// поглощал бы время ожидания upstream и делал <see cref="ForwardTimeout"/> недостижимым.
    /// </summary>
    private static readonly TimeSpan ConnectionIoTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Бюджет ожидания ответа upstream. Не связан с фазами обмена с клиентом.</summary>
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Заголовок, которым расширение помечает вкладку-источник запроса.
    /// </summary>
    /// <remarks>
    /// Chromium не отдаёт расширению прокси-аутентификацию (событие onAuthRequired в режиме
    /// asyncBlocking не приходит), поэтому route token там доставляется не через
    /// <c>Proxy-Authorization</c>, а этим заголовком: его ставит правило declarativeNetRequest,
    /// привязанное к конкретной вкладке. Заголовок входит в список hop-by-hop и срезается до
    /// отправки на origin — сайт не должен видеть никаких следов автоматизации.
    /// </remarks>
    internal const string RouteTokenHeaderName = "X-Atom-Route";

    private static readonly HashSet<string> HopByHopHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Proxy-Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        RouteTokenHeaderName,
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
    private readonly Func<ProxyNavigationRoute, string, string, string, string, IReadOnlyDictionary<string, string>?, CancellationToken, ValueTask>? interceptionDispatcher = interceptionDispatcher;
    private readonly ConcurrentDictionary<string, HttpClient> forwardClients = new(StringComparer.Ordinal);
    private Task? acceptLoop;
    private bool isDisposed;

    public int Port { get; private set; } = port;

    /// <summary>
    /// Требовать ли route token через 407 Proxy Authentication Required.
    /// </summary>
    /// <remarks>
    /// Firefox доставляет токен только так: он отвечает на вызов через blocking onAuthRequired.
    /// Chromium прокси-аутентификацию расширению не отдаёт и токен приносит заголовком, поэтому
    /// для него вызов выключается — иначе первый же запрос вкладки без токена был бы отклонён,
    /// хотя через прокси идёт весь трафик браузера, включая заведомо неотслеживаемый.
    /// </remarks>
    internal bool ChallengeForRouteToken { get; set; } = true;

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
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>
    /// Ограничивает одну фазу обмена с клиентом. Токен сервера остаётся связанным, поэтому
    /// остановка сервера прерывает фазу немедленно.
    /// </summary>
    private static CancellationTokenSource CreatePhaseBudget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ConnectionIoTimeout);
        return budget;
    }

#pragma warning disable MA0051 // Method is too long
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
#pragma warning restore MA0051 // Method is too long
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                ProxyRequestReadResult readResult;
                using (var readBudget = CreatePhaseBudget(cancellationToken))
                {
                    readResult = await ReadProxyRequestAsync(stream, readBudget.Token).ConfigureAwait(false);
                }

                if (readResult.Request is not { } request)
                {
                    await WriteReadFailureAsync(stream, readResult.Outcome, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (await TryHandleDirectRequestAsync(stream, request, cancellationToken).ConfigureAwait(false))
                    return;

                var routeToken = TryReadRouteToken(request.Headers);

                if (request.IsConnect)
                {
                    // На самом CONNECT токена может не быть: declarativeNetRequest правит заголовки
                    // запроса, а не установку туннеля. Поэтому туннель принимается безусловно, а
                    // маршрут определяется по запросу внутри TLS, где заголовок уже есть.
                    await HandleConnectTunnelAsync(stream, request, routeToken, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!TryResolveRouteForRequest(routeToken, out var route))
                {
                    if (ChallengeForRouteToken && string.IsNullOrWhiteSpace(routeToken))
                    {
                        logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "proxy-auth-missing");
                        await WriteProxyAuthenticationRequiredAsync(stream, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    // Через прокси идёт весь трафик браузера, а не только перехватываемые навигации:
                    // у запросов вне отслеживаемых вкладок токена нет и быть не может. Такой запрос
                    // прозрачно форвардится — отклонять его значило бы ломать обычную загрузку страниц.
                    await ForwardUnroutedRequestAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var registry = registryResolver();
                if (registry is null)
                {
                    await ForwardUnroutedRequestAsync(stream, request, cancellationToken).ConfigureAwait(false);
                    return;
                }


                if (!TryBuildAbsoluteTargetUrl("http", request.Target, request.Headers, fallbackHost: null, fallbackPort: 0, out var absoluteTargetUrl))
                {
                    logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "absolute-url-invalid");
                    await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-target", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await HandleNavigationRequestAsync(stream, request, absoluteTargetUrl, route.RouteToken, route, registry, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Сообщает клиенту причину отказа в разборе запроса. Прежняя версия молча закрывала
    /// соединение или, что хуже, форвардила запрос с потерянным либо обрезанным телом.
    /// </summary>
    private async Task WriteReadFailureAsync(Stream stream, ProxyRequestReadOutcome outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case ProxyRequestReadOutcome.HeadersTooLarge:
                await WriteErrorResponseAsync(stream, HttpStatusCode.RequestHeaderFieldsTooLarge, "request-headers-too-large", cancellationToken).ConfigureAwait(false);
                return;

            case ProxyRequestReadOutcome.BodyTooLarge:
                await WriteErrorResponseAsync(stream, HttpStatusCode.RequestEntityTooLarge, "request-body-too-large", cancellationToken).ConfigureAwait(false);
                return;

            case ProxyRequestReadOutcome.Malformed:
                await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "malformed-request", cancellationToken).ConfigureAwait(false);
                return;

            default:
                // Клиент закрыл соединение, не прислав запрос: отвечать некому.
                return;
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
        string? connectRouteToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectHost) || request.ConnectPort <= 0)
        {
            logger?.LogBridgeServerNavigationProxyRejected(request.Method, request.Target, "connect-target-invalid");
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-connect-target", cancellationToken).ConfigureAwait(false);
            return;
        }

        using var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);

        using (var handshakeBudget = CreatePhaseBudget(cancellationToken))
        {
            await WriteConnectEstablishedAsync(stream, handshakeBudget.Token).ConfigureAwait(false);

            var certificate = BridgeManagedDeliveryCertificateManager.Instance.GetOrCreateCertificate(request.ConnectHost);
            await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, handshakeBudget.Token).ConfigureAwait(false);
        }

        ProxyRequestReadResult tunneledReadResult;
        using (var readBudget = CreatePhaseBudget(cancellationToken))
        {
            tunneledReadResult = await ReadProxyRequestAsync(sslStream, readBudget.Token).ConfigureAwait(false);
        }

        if (tunneledReadResult.Request is not { } tunneledRequest)
        {
            await WriteReadFailureAsync(sslStream, tunneledReadResult.Outcome, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Цель закреплена за хостом из CONNECT: заголовок Host внутри туннеля подконтролен
        // содержимому страницы и не должен уводить запрос на другой origin.
        if (!TryBuildTunneledTargetUrl(tunneledRequest.Target, request.ConnectHost, request.ConnectPort, out var absoluteTargetUrl))
        {
            logger?.LogBridgeServerNavigationProxyRejected(tunneledRequest.Method, tunneledRequest.Target, "tunnel-target-invalid");
            await WriteErrorResponseAsync(sslStream, HttpStatusCode.BadRequest, "invalid-tunnel-target", cancellationToken).ConfigureAwait(false);
            return;
        }

        // Токен запроса внутри туннеля приоритетнее: именно на нём стоит заголовок вкладки.
        var routeToken = TryReadRouteToken(tunneledRequest.Headers) ?? connectRouteToken;
        if (!TryResolveRouteForRequest(routeToken, out var route))
        {
            if (ChallengeForRouteToken && string.IsNullOrWhiteSpace(routeToken))
            {
                logger?.LogBridgeServerNavigationProxyRejected(tunneledRequest.Method, tunneledRequest.Target, "proxy-auth-missing");
                await WriteProxyAuthenticationRequiredAsync(sslStream, cancellationToken).ConfigureAwait(false);
                return;
            }

            await ForwardUnroutedRequestAsync(sslStream, tunneledRequest, cancellationToken, absoluteTargetUrl).ConfigureAwait(false);
            return;
        }

        var tunnelRegistry = registryResolver();
        if (tunnelRegistry is null)
        {
            await ForwardUnroutedRequestAsync(sslStream, tunneledRequest, cancellationToken, absoluteTargetUrl).ConfigureAwait(false);
            return;
        }

        await HandleNavigationRequestAsync(sslStream, tunneledRequest, absoluteTargetUrl, route.RouteToken, route, tunnelRegistry, cancellationToken).ConfigureAwait(false);
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
            // Решения нет — возможно, спросить драйвер ещё некому: в Chromium блокирующего
            // webRequest не существует, поэтому перехват поднимает сам прокси и повторяет выборку.
            await TryDispatchInterceptionAsync(clientRequest, absoluteTargetUrl, route, cancellationToken).ConfigureAwait(false);
            _ = TryConsumeDecision(registry, routeToken, clientRequest.Method, absoluteTargetUrl, DateTimeOffset.UtcNow, out decision);
        }

        if (decision is null)
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

#pragma warning disable CA1873 // Избегайте потенциально ресурсоемкого ведения журнала
        logger?.LogBridgeServerNavigationProxyMatched(decision.Action.ToString(), clientRequest.Method, absoluteTargetUrl);
#pragma warning restore CA1873 // Избегайте потенциально ресурсоемкого ведения журнала

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
                // Навигационный abort должен ОТМЕНИТЬ переход и оставить вкладку на текущей странице,
                // а не коммитить переход. Любой обычный HTTP-ответ (403 + тело) браузер рендерит как
                // страницу по целевому URL. 204 No Content Firefox трактует как NS_ERROR_NO_CONTENT:
                // навигация не коммитится, error page не показывается, вкладка остаётся на исходном URL.
                await WriteDecisionResponseAsync(
                    stream,
                    method,
                    (int)HttpStatusCode.NoContent,
                    reasonPhrase: null,
                    responseHeaders: null,
                    responseBody: null,
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

    private enum ProxyRequestReadOutcome
    {
        Request,
        Closed,
        Malformed,
        HeadersTooLarge,
        BodyTooLarge,
    }

    private readonly record struct ProxyRequestReadResult(ProxyRequestReadOutcome Outcome, ProxyRequest? Request)
    {
        public static ProxyRequestReadResult Failure(ProxyRequestReadOutcome outcome) => new(outcome, Request: null);

        public static ProxyRequestReadResult Success(ProxyRequest request) => new(ProxyRequestReadOutcome.Request, request);
    }

    private static async Task<ProxyRequestReadResult> ReadProxyRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var envelopeResult = await ReadRequestEnvelopeAsync(stream, cancellationToken).ConfigureAwait(false);
        if (envelopeResult.Envelope is not { } requestEnvelope)
            return ProxyRequestReadResult.Failure(envelopeResult.Outcome);

        var (headerBytes, bufferedBodyBytes) = requestEnvelope;
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var headerLines = headerText.Split("\r\n", StringSplitOptions.None);
        if (headerLines.Length == 0 || string.IsNullOrWhiteSpace(headerLines[0]))
            return ProxyRequestReadResult.Failure(ProxyRequestReadOutcome.Malformed);

        var parts = headerLines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return ProxyRequestReadResult.Failure(ProxyRequestReadOutcome.Malformed);

        var headers = ParseHeaders(headerLines);

        var method = parts[0];
        var target = parts[1];
        byte[]? bodyBytes;
        if (IsChunkedTransferEncoding(headers))
        {
            bodyBytes = await ReadChunkedBodyBytesAsync(stream, bufferedBodyBytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Content-Length читается как long: значение сверх int.MaxValue раньше не разбиралось
            // и запрос уходил upstream вообще без тела.
            var contentLength = TryReadContentLength(headers);
            if (contentLength is null)
                return ProxyRequestReadResult.Failure(ProxyRequestReadOutcome.Malformed);

            if (contentLength > MaxRequestBodyBytes)
                return ProxyRequestReadResult.Failure(ProxyRequestReadOutcome.BodyTooLarge);

            bodyBytes = contentLength > 0
                ? await ReadRequestBodyBytesAsync(stream, bufferedBodyBytes, (int)contentLength.Value, cancellationToken).ConfigureAwait(false)
                : [];
        }

        // Тело не прочитано целиком: форвардить обрезанный запрос нельзя — upstream получил бы
        // повреждённые данные и, вероятно, обработал бы их как валидные.
        return bodyBytes is null
            ? ProxyRequestReadResult.Failure(ProxyRequestReadOutcome.BodyTooLarge)
            : ProxyRequestReadResult.Success(CreateProxyRequest(method, target, headers, bodyBytes));
    }

    private static bool IsChunkedTransferEncoding(Dictionary<string, string> headers)
        => headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static token => string.Equals(token, "chunked", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Возвращает <see langword="null"/>, если тело не удалось прочитать целиком (обрыв,
    /// нарушенный chunked-формат или превышение лимита). Частично собранное тело наружу
    /// не отдаётся: молчаливая обрезка приводила бы к порче данных на upstream.
    /// </summary>
#pragma warning disable S3776 // Cognitive Complexity of methods should not be too high
    private static async Task<byte[]?> ReadChunkedBodyBytesAsync(Stream stream, byte[] bufferedBodyBytes, CancellationToken cancellationToken)
#pragma warning restore S3776 // Cognitive Complexity of methods should not be too high
    {
        var cursor = new ChunkCursor(stream, bufferedBodyBytes);
        var body = new MemoryStream();

        while (true)
        {
            var sizeLine = await cursor.ReadAsciiLineAsync(cancellationToken).ConfigureAwait(false);
            if (sizeLine is null)
                return null;

            var sizeText = sizeLine.Split(';', 2)[0].Trim();
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                return null;

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
                return null;

            if (!await cursor.CopyExactlyAsync(body, chunkSize, cancellationToken).ConfigureAwait(false))
                return null;

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

    private readonly record struct RequestEnvelopeReadResult(
        ProxyRequestReadOutcome Outcome,
        (byte[] HeaderBytes, byte[] BufferedBodyBytes)? Envelope);

    private static async Task<RequestEnvelopeReadResult> ReadRequestEnvelopeAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Буфер растёт удвоением вместо поэлементного List<byte>.Add: заголовки до 128 КБ
        // копировались по одному байту на каждый прочитанный октет.
        var buffer = new byte[4096];
        var count = 0;

        while (true)
        {
            if (count == buffer.Length)
            {
                if (buffer.Length >= MaxRequestHeaderBytes)
                    return new(ProxyRequestReadOutcome.HeadersTooLarge, Envelope: null);

                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxRequestHeaderBytes));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return new(
                    count == 0 ? ProxyRequestReadOutcome.Closed : ProxyRequestReadOutcome.Malformed,
                    Envelope: null);
            }

            var previousCount = count;
            count += read;

            if (TryExtractRequestEnvelope(buffer.AsSpan(0, count), previousCount, out var envelope))
                return new(ProxyRequestReadOutcome.Request, envelope);
        }
    }

    private static bool TryExtractRequestEnvelope(ReadOnlySpan<byte> buffer, int previousCount, out (byte[] HeaderBytes, byte[] BufferedBodyBytes) envelope)
    {
        // Разделитель мог начаться в предыдущей порции, поэтому поиск отступает на три байта.
        var searchStart = Math.Max(0, previousCount - 3);
        var separatorIndex = buffer[searchStart..].IndexOf("\r\n\r\n"u8);
        if (separatorIndex < 0)
        {
            envelope = default;
            return false;
        }

        var headerEnd = searchStart + separatorIndex;
        envelope = (
            buffer[..headerEnd].ToArray(),
            buffer[(headerEnd + 4)..].ToArray());
        return true;
    }

    // Синтетическое решение «продолжить как есть» для запросов без зарегистрированного решения.
    /// <summary>
    /// Пытается сопоставить запросу зарегистрированный маршрут по route token.
    /// </summary>
    private bool TryResolveRouteForRequest(string? routeToken, [NotNullWhen(true)] out ProxyNavigationRoute? route)
    {
        route = null;

        if (string.IsNullOrWhiteSpace(routeToken))
            return false;

        var registry = registryResolver();
        return registry is not null && registry.TryResolveRoute(routeToken, out route);
    }

    /// <summary>
    /// Прозрачно форвардит запрос, для которого нет отслеживаемого маршрута.
    /// </summary>
    /// <remarks>
    /// Такие запросы — норма: через прокси идёт весь трафик браузера, включая вкладки без
    /// перехвата и служебные обращения самого браузера. Решений для них нет, поэтому запрос
    /// уходит на origin как есть; upstream-прокси не применяется, так как маршрут неизвестен.
    /// </remarks>
    private async Task ForwardUnroutedRequestAsync(
        Stream stream,
        ProxyRequest clientRequest,
        CancellationToken cancellationToken,
        string? absoluteTargetUrl = null)
    {
        var targetUrl = absoluteTargetUrl;
        if (string.IsNullOrWhiteSpace(targetUrl)
            && !TryBuildAbsoluteTargetUrl("http", clientRequest.Target, clientRequest.Headers, fallbackHost: null, fallbackPort: 0, out targetUrl))
        {
            logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, clientRequest.Target, "absolute-url-invalid");
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadRequest, "invalid-target", cancellationToken).ConfigureAwait(false);
            return;
        }

        var unroutedRoute = new ProxyNavigationRoute
        {
            SessionId = string.Empty,
            TabId = string.Empty,
            ContextId = string.Empty,
            RouteToken = string.Empty,
        };

        await ForwardContinueDecisionAsync(
            stream,
            clientRequest,
            targetUrl,
            unroutedRoute,
            CreateImplicitContinueDecision(clientRequest, targetUrl),
            cancellationToken).ConfigureAwait(false);
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

    private async ValueTask TryDispatchInterceptionAsync(
        ProxyRequest clientRequest,
        string absoluteTargetUrl,
        ProxyNavigationRoute route,
        CancellationToken cancellationToken)
    {
        if (interceptionDispatcher is not { } dispatch)
            return;

        try
        {
            await dispatch(
                route,
                CreateProxyRequestId(),
                clientRequest.Method,
                absoluteTargetUrl,
                ResolveResourceType(clientRequest),
                clientRequest.Headers,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogBridgeServerNavigationProxyConnectionFailed(exception);
        }
    }

    private static string CreateProxyRequestId()
        => "proxy-" + Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture);

    /// <summary>
    /// Тип ресурса по заголовкам запроса: прокси не получает его от браузера напрямую.
    /// </summary>
    private static string ResolveResourceType(ProxyRequest clientRequest)
    {
        if (clientRequest.Headers.TryGetValue("Sec-Fetch-Dest", out var fetchDestination)
            && !string.IsNullOrWhiteSpace(fetchDestination))
        {
            return string.Equals(fetchDestination, "document", StringComparison.OrdinalIgnoreCase)
                ? "main_frame"
                : fetchDestination.Trim();
        }

        return clientRequest.Headers.TryGetValue("Accept", out var accept)
            && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                ? "main_frame"
                : "other";
    }

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

        // Бюджет upstream отсчитывается от токена сервера, а не от фазы обмена с клиентом:
        // иначе он поглощался бы уже потраченным на чтение запроса временем.
        using var forwardBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        forwardBudget.CancelAfter(ForwardTimeout);

        try
        {
            var client = GetForwardClient(route.UpstreamProxy);
            using var forwardRequest = CreateForwardRequest(clientRequest, decision, forwardTargetUrl);
            using var forwardResponse = await client.SendAsync(
                forwardRequest,
                HttpCompletionOption.ResponseHeadersRead,
                forwardBudget.Token).ConfigureAwait(false);

            var body = await ReadForwardResponseBodyAsync(forwardResponse, forwardBudget.Token).ConfigureAwait(false);
            if (body is null)
            {
                logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, absoluteTargetUrl, "forward-response-too-large");
                await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "forward-response-too-large", cancellationToken).ConfigureAwait(false);
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
            await WriteErrorResponseAsync(stream, HttpStatusCode.BadGateway, "decision-forward-failed", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogBridgeServerNavigationProxyRejected(clientRequest.Method, absoluteTargetUrl, "forward-timeout");
            await WriteErrorResponseAsync(stream, HttpStatusCode.GatewayTimeout, "forward-timeout", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Тело буферизуется целиком, потому что ответ клиенту всегда обрамляется Content-Length.
    /// Лимит проверяется по мере чтения, а не после него: ответ без Content-Length (chunked)
    /// иначе успевал бы полностью попасть в память до отбраковки.
    /// </summary>
    private static async Task<byte[]?> ReadForwardResponseBodyAsync(HttpResponseMessage forwardResponse, CancellationToken cancellationToken)
    {
        if (forwardResponse.Content.Headers.ContentLength is { } knownLength)
        {
            if (knownLength > MaxForwardResponseBytes)
                return null;

            if (knownLength == 0)
                return [];
        }

        var contentStream = await forwardResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (contentStream.ConfigureAwait(false))
        {
            using var body = new MemoryStream();
            var chunk = new byte[64 * 1024];

            while (true)
            {
                var read = await contentStream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    return body.ToArray();

                if (body.Length + read > MaxForwardResponseBytes)
                    return null;

                await body.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
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
            request.Content.Headers.ContentLength = body.Length;

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
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,

            // Клиенты кешируются на всё время жизни прокси, поэтому соединения нужно
            // периодически пересоздавать: иначе они держали бы устаревшие DNS-записи.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
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
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>
    /// <see langword="null"/> — заголовок присутствует, но не является корректным неотрицательным
    /// числом; <c>0</c> — тела нет.
    /// </summary>
    private static long? TryReadContentLength(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Content-Length", out var rawContentLength))
            return 0;

        return long.TryParse(rawContentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contentLength)
            && contentLength >= 0
                ? contentLength
                : null;
    }

    /// <summary>
    /// Возвращает <see langword="null"/>, если клиент закрыл поток до передачи всего тела.
    /// </summary>
    private static async Task<byte[]?> ReadRequestBodyBytesAsync(Stream stream, byte[] bufferedBodyBytes, int contentLength, CancellationToken cancellationToken)
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
                return null;

            offset += read;
        }

        return result;
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
        if (TryReadAbsoluteHttpTarget(rawTarget, out absoluteTargetUrl))
            return true;

        var authority = headers.TryGetValue("Host", out var hostHeader) && !string.IsNullOrWhiteSpace(hostHeader)
            ? hostHeader
            : FormatAuthority(fallbackHost, fallbackPort);

        return TryResolveAgainstAuthority(scheme, authority, rawTarget, out absoluteTargetUrl);
    }

    /// <summary>
    /// Строит цель запроса, пришедшего внутри CONNECT-туннеля. Authority берётся исключительно
    /// из адреса, для которого туннель был установлен: заголовок Host и absolute-form цель
    /// приходят из контента страницы и позволили бы увести запрос на посторонний узел,
    /// сохранив при этом уже выданное TLS-соединение и маршрут прокси.
    /// </summary>
    private static bool TryBuildTunneledTargetUrl(
        string rawTarget,
        string connectHost,
        int connectPort,
        [NotNullWhen(true)] out string? absoluteTargetUrl)
    {
        if (TryReadAbsoluteHttpTarget(rawTarget, out var absoluteTarget)
            && Uri.TryCreate(absoluteTarget, UriKind.Absolute, out var absoluteUri))
        {
            // Absolute-form внутри туннеля допустима только если она указывает на тот же узел.
            // Сравнение по компонентам, а не по строке authority: Uri опускает порт по умолчанию.
            if (!string.Equals(absoluteUri.Host, connectHost, StringComparison.OrdinalIgnoreCase)
                || absoluteUri.Port != connectPort)
            {
                absoluteTargetUrl = null;
                return false;
            }

            absoluteTargetUrl = absoluteTarget;
            return true;
        }

        return TryResolveAgainstAuthority(Uri.UriSchemeHttps, FormatAuthority(connectHost, connectPort), rawTarget, out absoluteTargetUrl);
    }

    private static bool TryReadAbsoluteHttpTarget(string rawTarget, [NotNullWhen(true)] out string? absoluteTargetUrl)
    {
        if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var absoluteUri)
            && (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            absoluteTargetUrl = absoluteUri.AbsoluteUri;
            return true;
        }

        absoluteTargetUrl = null;
        return false;
    }

    private static bool TryResolveAgainstAuthority(
        string scheme,
        string? authority,
        string rawTarget,
        [NotNullWhen(true)] out string? absoluteTargetUrl)
    {
        if (string.IsNullOrWhiteSpace(authority)
            || !Uri.TryCreate(string.Concat(scheme, "://", authority), UriKind.Absolute, out var baseUri)
            || !Uri.TryCreate(baseUri, NormalizeRelativeTarget(rawTarget), out var resolvedUri))
        {
            absoluteTargetUrl = null;
            return false;
        }

        absoluteTargetUrl = resolvedUri.AbsoluteUri;
        return true;
    }

    private static string? FormatAuthority(string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        return port > 0
            ? string.Concat(host, ":", port.ToString(CultureInfo.InvariantCulture))
            : host;
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
        // Chromium-путь: токен проставлен правилом declarativeNetRequest прямо на запросе.
        if (headers.TryGetValue(RouteTokenHeaderName, out var routeHeader)
            && !string.IsNullOrWhiteSpace(routeHeader))
        {
            return routeHeader.Trim();
        }

        // Firefox-путь: токен приезжает как имя пользователя в ответе на 407.
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

        // Запись — самостоятельная фаза со своим бюджетом: клиент, переставший читать сокет,
        // не должен удерживать обработчик после того, как ответ уже получен от upstream.
        using var writeBudget = CreatePhaseBudget(cancellationToken);
        await stream.WriteAsync(headerBytes, writeBudget.Token).ConfigureAwait(false);

        if (includeBody && effectiveBody.Length > 0)
            await stream.WriteAsync(effectiveBody, writeBudget.Token).ConfigureAwait(false);

        await stream.FlushAsync(writeBudget.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Порядок кандидатов детерминирован: точное совпадение проверяется первым. Потребление
    /// решения деструктивно, поэтому от порядка зависит, какое именно решение будет применено.
    /// </summary>
    private static List<string> EnumerateDecisionLookupUrls(string absoluteTargetUrl)
    {
        List<string> candidates = [absoluteTargetUrl];

        if (!Uri.TryCreate(absoluteTargetUrl, UriKind.Absolute, out var uri))
            return candidates;

        var defaultPort = uri.Scheme switch
        {
            "http" => 80,
            "https" => 443,
            _ => 0,
        };

        if (defaultPort <= 0)
            return candidates;

        AddCandidate(candidates, new UriBuilder(uri) { Port = -1 }.Uri.AbsoluteUri);
        AddCandidate(candidates, new UriBuilder(uri) { Port = defaultPort }.Uri.AbsoluteUri);
        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            candidates.Add(candidate);
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