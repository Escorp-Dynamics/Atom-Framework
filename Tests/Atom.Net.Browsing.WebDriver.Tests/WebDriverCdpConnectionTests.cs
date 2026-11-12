using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol.Cdp;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверяет CDP-соединение против серверной стороны WebSocket в памяти:
/// сопоставление ответов, ошибки, события, flat-сессии, тайм-ауты и разрыв соединения.
/// </summary>
[TestFixture]
public sealed class WebDriverCdpConnectionTests
{
    [Test]
    public async Task ResponsesAreMatchedByIdEvenWhenServerRepliesOutOfOrder()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);

        var first = connection.SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = "1" }).AsTask();
        var second = connection.SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = "2" }).AsTask();

        var request1 = await pair.ReceiveAsync().ConfigureAwait(false);
        var request2 = await pair.ReceiveAsync().ConfigureAwait(false);

        await pair.SendAsync(new JsonObject { ["id"] = request2["id"]!.GetValue<long>(), ["result"] = new JsonObject { ["value"] = "second" } }).ConfigureAwait(false);
        await pair.SendAsync(new JsonObject { ["id"] = request1["id"]!.GetValue<long>(), ["result"] = new JsonObject { ["value"] = "first" } }).ConfigureAwait(false);

        var firstResult = await first.ConfigureAwait(false);
        var secondResult = await second.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(request1["method"]!.GetValue<string>(), Is.EqualTo("Runtime.evaluate"));
            Assert.That(request1["params"]!["expression"]!.GetValue<string>(), Is.EqualTo("1"));
            Assert.That(request1.ContainsKey("sessionId"), Is.False);
            Assert.That(firstResult.GetProperty("value").GetString(), Is.EqualTo("first"));
            Assert.That(secondResult.GetProperty("value").GetString(), Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task SessionIdIsSentAndServerErrorBecomesCdpException()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);

        var call = connection.SendAsync("DOM.getDocument", parameters: null, sessionId: "page-1-session-1").AsTask();
        var request = await pair.ReceiveAsync().ConfigureAwait(false);
        await pair.SendAsync(new JsonObject
        {
            ["id"] = request["id"]!.GetValue<long>(),
            ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "No page" },
        }).ConfigureAwait(false);

        var exception = await AsyncAssert.ThrowsAsync<CdpException>(async () => await call.ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(request["sessionId"]!.GetValue<string>(), Is.EqualTo("page-1-session-1"));
            Assert.That(request["params"], Is.InstanceOf<JsonObject>());
            Assert.That(exception!.Method, Is.EqualTo("DOM.getDocument"));
            Assert.That(exception.Code, Is.EqualTo(-32000));
            Assert.That(exception.ServerMessage, Is.EqualTo("No page"));
        });
    }

    [Test]
    public async Task EventsAreDeliveredWithSessionAndDetachedParameters()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);
        var received = new TaskCompletionSource<CdpEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.EventReceived += cdpEvent => received.TrySetResult(cdpEvent);

        await pair.SendAsync(new JsonObject
        {
            ["method"] = "Runtime.bindingCalled",
            ["params"] = new JsonObject { ["name"] = "__atomCb", ["payload"] = "payload-1" },
            ["sessionId"] = "page-1-session-1",
        }).ConfigureAwait(false);

        var cdpEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(cdpEvent.Method, Is.EqualTo("Runtime.bindingCalled"));
            Assert.That(cdpEvent.SessionId, Is.EqualTo("page-1-session-1"));
            Assert.That(cdpEvent.Params.GetProperty("payload").GetString(), Is.EqualTo("payload-1"));
        });
    }

    [Test]
    public async Task LargeFragmentedResponseIsReassembled()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);

        var call = connection.SendAsync("Page.captureScreenshot").AsTask();
        var request = await pair.ReceiveAsync().ConfigureAwait(false);
        var data = new string('A', 300_000);
        var json = new JsonObject { ["id"] = request["id"]!.GetValue<long>(), ["result"] = new JsonObject { ["data"] = data } }.ToJsonString();
        await pair.SendFragmentedAsync(Encoding.UTF8.GetBytes(json), fragmentSize: 7_000).ConfigureAwait(false);

        var result = await call.ConfigureAwait(false);

        Assert.That(result.GetProperty("data").GetString(), Has.Length.EqualTo(data.Length));
    }

    [Test]
    public async Task CommandTimesOutWhenServerDoesNotReply()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);
        connection.CommandTimeout = TimeSpan.FromMilliseconds(200);

        var exception = await AsyncAssert.ThrowsAsync<TimeoutException>(async () => await connection.SendAsync("Page.navigate").ConfigureAwait(false)).ConfigureAwait(false);

        Assert.That(exception.Message, Does.Contain("Page.navigate"));
    }

    [Test]
    public async Task PendingCommandsFailAndClosedFiresWhenServerDisconnects()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => closed.TrySetResult();

        var call = connection.SendAsync("Page.navigate").AsTask();
        _ = await pair.ReceiveAsync().ConfigureAwait(false);
        await pair.CloseServerAsync().ConfigureAwait(false);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var exception = await AsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await call.ConfigureAwait(false)).ConfigureAwait(false);
        _ = await AsyncAssert.ThrowsAsync<InvalidOperationException>(async () => await connection.SendAsync("Page.reload").ConfigureAwait(false)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("закрыто"));
            Assert.That(connection.IsOpen, Is.False);
        });
    }

    [Test]
    public async Task ThrowingEventSubscriberDoesNotBreakReceiveLoop()
    {
        await using var pair = await SocketPair.CreateAsync().ConfigureAwait(false);
        await using var connection = CdpConnection.Attach(pair.Client);
        connection.EventReceived += _ => throw new InvalidOperationException("subscriber-failure");

        await pair.SendAsync(new JsonObject { ["method"] = "Runtime.bindingCalled", ["params"] = new JsonObject() }).ConfigureAwait(false);

        var call = connection.SendAsync("Runtime.evaluate").AsTask();
        var request = await pair.ReceiveAsync().ConfigureAwait(false);
        await pair.SendAsync(new JsonObject { ["id"] = request["id"]!.GetValue<long>(), ["result"] = new JsonObject { ["value"] = "alive" } }).ConfigureAwait(false);

        var result = await call.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("value").GetString(), Is.EqualTo("alive"));
            Assert.That(connection.IsOpen, Is.True);
        });
    }

    [Test]
    public async Task ConnectAsyncSendsBearerToken()
    {
        await using var server = await HttpWebSocketServer.StartAsync().ConfigureAwait(false);

        await using var connection = await CdpConnection.ConnectAsync(server.Endpoint, "secret-token", CancellationToken.None).ConfigureAwait(false);

        Assert.That(await server.AuthorizationHeader.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.EqualTo("Bearer secret-token"));
    }

    /// <summary>
    /// Пара соединённых WebSocket в памяти: клиентская сторона для CdpConnection и серверная для теста.
    /// </summary>
    private sealed class SocketPair : IAsyncDisposable
    {
        private readonly HttpWebSocketServer server;

        private SocketPair(HttpWebSocketServer server, ClientWebSocket client, WebSocket serverSocket)
        {
            this.server = server;
            Client = client;
            Server = serverSocket;
        }

        public ClientWebSocket Client { get; }

        public WebSocket Server { get; }

        public static async Task<SocketPair> CreateAsync()
        {
            var server = await HttpWebSocketServer.StartAsync().ConfigureAwait(false);
            var client = new ClientWebSocket();
            await client.ConnectAsync(server.Endpoint, CancellationToken.None).ConfigureAwait(false);
            var serverSocket = await server.Accepted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return new SocketPair(server, client, serverSocket);
        }

        public async Task<JsonObject> ReceiveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            ValueWebSocketReceiveResult result;
            do
            {
                result = await Server.ReceiveAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false);
                buffer.Write(chunk, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return JsonNode.Parse(buffer.ToArray())!.AsObject();
        }

        public Task SendAsync(JsonObject message)
            => Server.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        public async Task SendFragmentedAsync(byte[] payload, int fragmentSize)
        {
            for (var offset = 0; offset < payload.Length; offset += fragmentSize)
            {
                var count = Math.Min(fragmentSize, payload.Length - offset);
                var last = offset + count >= payload.Length;
                await Server.SendAsync(payload.AsMemory(offset, count), WebSocketMessageType.Text, last, CancellationToken.None).ConfigureAwait(false);
            }
        }

        public async Task CloseServerAsync()
            => await Server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Server.Dispose();
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class HttpWebSocketServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly TaskCompletionSource<WebSocket> accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> authorization = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private HttpWebSocketServer(HttpListener listener, Uri endpoint)
        {
            this.listener = listener;
            Endpoint = endpoint;
            _ = AcceptAsync();
        }

        public Uri Endpoint { get; }

        public Task<WebSocket> Accepted => accepted.Task;

        public Task<string?> AuthorizationHeader => authorization.Task;

        public static Task<HttpWebSocketServer> StartAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var port = Random.Shared.Next(20000, 60000);
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    listener.Start();
                    return Task.FromResult(new HttpWebSocketServer(listener, new Uri($"ws://127.0.0.1:{port}/devtools/browser")));
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
            }

            throw new InvalidOperationException("Не удалось занять порт для тестового WebSocket-сервера");
        }

        public ValueTask DisposeAsync()
        {
            listener.Close();
            return ValueTask.CompletedTask;
        }

        private async Task AcceptAsync()
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                authorization.TrySetResult(context.Request.Headers["Authorization"]);
                var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                accepted.TrySetResult(webSocketContext.WebSocket);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                accepted.TrySetException(exception);
                authorization.TrySetException(exception);
            }
        }
    }
}
