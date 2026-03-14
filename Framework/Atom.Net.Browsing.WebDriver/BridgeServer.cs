using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// WebSocket-сервер, обеспечивающий мост между .NET-драйвером и расширением браузера.
/// </summary>
/// <remarks>
/// Каждая вкладка браузера подключается по отдельному WebSocket-каналу,
/// что обеспечивает полную изоляцию — словно каждая вкладка работает
/// в собственном экземпляре процесса браузера.
/// </remarks>
/// <param name="settings">Настройки моста.</param>
internal sealed class BridgeServer(BridgeSettings settings) : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, TabChannel> channels = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InterceptedRequestFulfillment> pendingFulfillments = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private Task? acceptLoop;
    private Task? pingLoop;
    private bool isDisposed;

    /// <summary>
    /// Фактический порт, на котором запущен сервер.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Количество подключённых вкладок.
    /// </summary>
    public int ConnectionCount => channels.Count;

    /// <summary>
    /// Происходит при подключении новой вкладки.
    /// </summary>
    public event AsyncEventHandler<BridgeServer, TabConnectedEventArgs>? TabConnected;

    /// <summary>
    /// Происходит при отключении вкладки.
    /// </summary>
    public event AsyncEventHandler<BridgeServer, TabDisconnectedEventArgs>? TabDisconnected;

    /// <summary>
    /// Происходит при получении события от любой вкладки.
    /// </summary>
    public event AsyncEventHandler<BridgeServer, TabChannelEventArgs>? EventReceived;

    /// <summary>
    /// Происходит при перехвате сетевого запроса расширением.
    /// Обработчик должен вызвать <see cref="InterceptedRequestEventArgs.Continue()"/>,
    /// <see cref="InterceptedRequestEventArgs.Abort()"/> или
    /// <see cref="InterceptedRequestEventArgs.Fulfill(InterceptedRequestFulfillment)"/>.
    /// </summary>
    public event AsyncEventHandler<BridgeServer, InterceptedRequestEventArgs>? RequestIntercepted;

    /// <summary>
    /// Запускает WebSocket-сервер.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        Port = settings.Port == 0 ? FindFreePort() : settings.Port;
        var prefix = string.Concat("http://", settings.Host, ":", Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "/");
        listener.Prefixes.Add(prefix);
        listener.Start();

        acceptLoop = Task.Run(() => AcceptLoopAsync(cts.Token), cancellationToken);
        pingLoop = Task.Run(() => PingLoopAsync(cts.Token), cancellationToken);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Возвращает канал вкладки по идентификатору.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    /// <returns>Канал вкладки или <see langword="null"/>, если вкладка не подключена.</returns>
    public TabChannel? GetChannel(string tabId) => channels.GetValueOrDefault(tabId);

    /// <summary>
    /// Возвращает все активные каналы.
    /// </summary>
    public IEnumerable<TabChannel> GetChannels() => channels.Values;

    /// <summary>
    /// Ожидает подключения вкладки с указанным идентификатором.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Канал подключённой вкладки.</returns>
    public async ValueTask<TabChannel> WaitForTabAsync(string tabId, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (channels.TryGetValue(tabId, out var channel))
                return channel;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(context, cancellationToken), cancellationToken);
        }
    }

#pragma warning disable MA0051 // Диспетчер подключений маршрутизирует HTTP и WebSocket.
    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
#pragma warning restore MA0051
    {
        if (!context.Request.IsWebSocketRequest)
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            if (string.Equals(path, "/intercept", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInterceptRequestAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/fulfill/", StringComparison.OrdinalIgnoreCase))
            {
                HandleFulfillRequest(context, path);
                return;
            }

            HandleHttpRequest(context);
            return;
        }

        var secret = context.Request.QueryString["secret"];
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(secret ?? string.Empty),
                Encoding.UTF8.GetBytes(settings.Secret)))
        {
            context.Response.StatusCode = 403;
            context.Response.Close();
            return;
        }

        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            return;
        }

        var handshake = await ReceiveHandshakeAsync(wsContext.WebSocket, cancellationToken).ConfigureAwait(false);
        if (handshake?.TabId is not { Length: > 0 } tabId)
        {
            await wsContext.WebSocket.CloseAsync(
                WebSocketCloseStatus.ProtocolError,
                "Отсутствует идентификатор вкладки в handshake.",
                CancellationToken.None).ConfigureAwait(false);
            wsContext.WebSocket.Dispose();
            return;
        }

        var channel = new TabChannel(tabId, wsContext.WebSocket, settings.RequestTimeout, settings.MaxMessageSize);
        channel.EventReceived += OnTabEventReceived;
        channel.Disconnected += OnTabDisconnected;

        // Если вкладка с таким ID уже подключена — отключаем старую.
        if (channels.TryRemove(tabId, out var previous))
            await previous.DisposeAsync().ConfigureAwait(false);

        channels[tabId] = channel;
        channel.StartReceiving();

        if (TabConnected is { } handler)
            await handler(this, new TabConnectedEventArgs(tabId, channel)).ConfigureAwait(false);
    }

    private async ValueTask OnTabEventReceived(TabChannel sender, TabChannelEventArgs e)
    {
        if (EventReceived is { } handler)
            await handler(this, e).ConfigureAwait(false);
    }

    private async ValueTask OnTabDisconnected(TabChannel sender, EventArgs e)
    {
        channels.TryRemove(sender.TabId, out _);

        if (TabDisconnected is { } handler)
            await handler(this, new TabDisconnectedEventArgs(sender.TabId)).ConfigureAwait(false);
    }

    private static async ValueTask<BridgeMessage?> ReceiveHandshakeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (result.MessageType != WebSocketMessageType.Text)
            return null;

        return JsonSerializer.Deserialize(
            buffer.AsSpan(0, result.Count),
            BridgeJsonContext.Default.BridgeMessage);
    }

    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(settings.PingInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var channel in channels.Values)
            {
                if (!channel.IsConnected)
                    continue;

                try
                {
                    await channel.SendCommandAsync(
                        BridgeCommand.GetUrl,
                        payload: null,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (BridgeException)
                {
                    // Канал мёртв — будет удалён через Disconnected-событие.
                }
            }
        }
    }

    private async Task HandleInterceptRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");

        InterceptHttpRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(
                context.Request.InputStream,
                InterceptJsonContext.Default.InterceptHttpRequest,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        if (request?.RequestId is null || request.TabId is null)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        var args = new InterceptedRequestEventArgs
        {
            RequestId = request.RequestId,
            Url = request.Url ?? string.Empty,
            Method = request.Method ?? "GET",
            ResourceType = request.Type ?? "other",
            TabId = request.TabId,
            PostData = request.RequestBodyBase64 is { } b64 ? Convert.FromBase64String(b64) : null,
            FormData = request.FormData,
            Timestamp = request.Timestamp is { } ts
                ? DateTimeOffset.FromUnixTimeMilliseconds(ts)
                : DateTimeOffset.UtcNow,
        };

        if (RequestIntercepted is { } handler)
            await handler(this, args).ConfigureAwait(false);

        args.SetDefaultIfPending();

        var decision = await args.WaitForDecisionAsync(cancellationToken).ConfigureAwait(false);
        var response = BuildInterceptResponse(decision, request.RequestId);

        context.Response.ContentType = "application/json";
        var json = JsonSerializer.SerializeToUtf8Bytes(response, InterceptJsonContext.Default.InterceptHttpResponse);
        context.Response.ContentLength64 = json.Length;
        await context.Response.OutputStream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        context.Response.Close();
    }

    /// <summary>
    /// Предварительно регистрирует fulfillment и возвращает URL для его получения через <c>/fulfill/</c>.
    /// Используется для подмены тела страницы: C# регистрирует контент до навигации,
    /// а расширение перенаправляет main_frame запрос на этот URL.
    /// </summary>
    internal string RegisterFulfillment(InterceptedRequestFulfillment fulfillment)
    {
        var id = Guid.NewGuid().ToString("N");
        pendingFulfillments[id] = fulfillment;
        return string.Concat("http://127.0.0.1:", Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "/fulfill/", id);
    }

    private InterceptHttpResponse BuildInterceptResponse(InterceptDecision decision, string requestId)
    {
        var response = new InterceptHttpResponse { Action = decision.Action.ToString().ToLowerInvariant() };

        if (decision.Action is InterceptAction.Continue && decision.Continuation is { } cont)
        {
            response.Url = cont.Url;
            response.Headers = cont.Headers;
            response.ResponseHeaders = cont.ResponseHeaders;
        }
        else if (decision.Action is InterceptAction.Fulfill && decision.Fulfillment is { } fulfillment)
        {
            pendingFulfillments[requestId] = fulfillment;
            response.Url = string.Concat("http://127.0.0.1:", Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "/fulfill/", requestId);
        }

        return response;
    }

    private void HandleFulfillRequest(HttpListenerContext context, string path)
    {
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");

        // path = "/fulfill/{requestId}"
        var requestId = path["/fulfill/".Length..];

        if (!pendingFulfillments.TryRemove(requestId, out var fulfillment))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        context.Response.StatusCode = fulfillment.StatusCode;
        context.Response.ContentType = fulfillment.ContentType;

        if (fulfillment.Headers is { } headers)
        {
            foreach (var (key, value) in headers)
                context.Response.AddHeader(key, value);
        }

        if (fulfillment.Body is { } body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
        }

        context.Response.Close();
    }

    private void HandleHttpRequest(HttpListenerContext context)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");

        var path = context.Request.Url?.AbsolutePath ?? "/";

        if (string.Equals(path, "/blank", StringComparison.OrdinalIgnoreCase))
        {
            // Минимальная пустая страница — замена about:blank.
            // Content.js inject'ится автоматически (http URL → match <all_urls>).
            var blank = "<!DOCTYPE html><html><head><title></title></head><body></body></html>"u8;
            context.Response.ContentLength64 = blank.Length;
            context.Response.OutputStream.Write(blank);
            context.Response.Close();
            return;
        }

        // Discovery endpoint: расширению нужна HTML-страница, чтобы content.js загрузился.
        context.Response.AddHeader("Content-Security-Policy", "default-src * 'unsafe-inline' 'unsafe-eval' data: blob:;");

        var port = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var html = Encoding.UTF8.GetBytes(
            $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta name="atom-bridge-port" content="{{port}}">
            <meta name="atom-bridge-secret" content="{{settings.Secret}}">
            <title>Atom Bridge Discovery</title>
            <script>
            new MutationObserver(function(muts){
                for(var i=0;i<muts.length;i++){
                    var nodes=muts[i].addedNodes;
                    for(var j=0;j<nodes.length;j++){
                        var n=nodes[j];
                        if(n.nodeType===1&&n.id&&n.id.indexOf('__atom_r_')===0&&n.dataset.code!==undefined){
                            try{var r;try{r=(0,eval)('('+n.dataset.code+')');}catch(ex){if(ex instanceof SyntaxError)try{r=(0,eval)(n.dataset.code);}catch(ex2){if(ex2 instanceof SyntaxError)r=new Function(n.dataset.code)();else throw ex2;}else throw ex;}n.dataset.s='ok';n.dataset.v=r!=null?String(r):'';}
                            catch(e){n.dataset.s='err';n.dataset.v=e.message;}
                        }
                    }
                }
            }).observe(document.documentElement,{childList:true,subtree:true});
            </script>
            </head>
            <body></body>
            </html>
            """);

        context.Response.ContentLength64 = html.Length;
        context.Response.OutputStream.Write(html);
        context.Response.Close();
    }

    private static int FindFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);

        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await cts.CancelAsync().ConfigureAwait(false);

        if (acceptLoop is not null)
        {
#pragma warning disable VSTHRD003 // Задача запущена в нашем контексте через StartAsync.
            try { await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Штатное завершение. */ }
            catch (TimeoutException) { /* Цикл не завершился за 5 с — пропускаем. */ }
#pragma warning restore VSTHRD003
        }

        listener.Stop();

        foreach (var channel in channels.Values)
            await channel.DisposeAsync().ConfigureAwait(false);

        channels.Clear();

        if (pingLoop is not null)
        {
#pragma warning disable VSTHRD003 // Задача запущена в нашем контексте через StartAsync.
            try { await pingLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Штатное завершение. */ }
            catch (TimeoutException) { /* Цикл не завершился за 5 с — пропускаем. */ }
#pragma warning restore VSTHRD003
        }

        listener.Close();
        cts.Dispose();
    }
}
