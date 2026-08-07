using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты общего runtime-базового класса для биржевых клиентов реального времени.
/// </summary>
public class ExchangeClientBaseTests(ILogger logger) : BenchmarkTests<ExchangeClientBaseTests>(logger)
{
    public ExchangeClientBaseTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "ExchangeClientBase: SubscribeAsync отправляет только новые подписки")]
    public async Task SubscribeSendsOnlyNewSubscriptions()
    {
        using var client = new TestExchangeClient();

        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT"]);
        await client.SubscribeAsync(["ETHUSDT", "BTCUSDT", "SOLUSDT"]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.EqualTo(1));
        Assert.That(client.SentMessages, Is.EqualTo(["sub:BTCUSDT,ETHUSDT", "sub:SOLUSDT"]));
        Assert.That(client.Subscriptions, Is.EquivalentTo(["BTCUSDT", "ETHUSDT", "SOLUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: SubscribeAsync поддерживает multi-payload подписку")]
    public async Task SubscribeSupportsMultiPayloadCommands()
    {
        using var client = new TestExchangeClient(usePerMarketCommands: true);

        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT"]);

        Assert.That(client.SentMessages, Is.EqualTo(["sub:BTCUSDT", "sub:ETHUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: UnsubscribeAsync удаляет и отправляет только существующие подписки")]
    public async Task UnsubscribeRemovesOnlyExistingSubscriptions()
    {
        using var client = new TestExchangeClient();

        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT", "SOLUSDT"]);
        await client.UnsubscribeAsync(["ETHUSDT", "DOGEUSDT"]);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.SentMessages, Does.Contain("unsub:ETHUSDT"));
        Assert.That(client.Subscriptions, Is.EquivalentTo(["BTCUSDT", "SOLUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: входящее сообщение публикует MarketUpdateReceived")]
    public async Task IncomingMessagePublishesMarketUpdate()
    {
        using var client = new TestExchangeClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectMessageAsync("update:BTCUSDT:100.5:101.5:101.0");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(100.5));
        Assert.That(received.Value.BestAsk, Is.EqualTo(101.5));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(101.0));
    }

    [TestCase(TestName = "ExchangeClientBase: binary frame может быть подготовлен до парсинга")]
    public async Task BinaryFrameCanBePreparedBeforeParsing()
    {
        using var client = new TestExchangeClient(acceptBinaryMessages: true);
        var updateReceived = new TaskCompletionSource<MarketRealtimeUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.MarketUpdateReceived += (sender, args) =>
        {
            updateReceived.TrySetResult(args.Update);
            return ValueTask.CompletedTask;
        };

        client.EnqueueBinaryMessage("update:BTCUSDT:100.5:101.5:101.0");
        await client.SubscribeAsync(["BTCUSDT"]);

        var update = await updateReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(update.AssetId, Is.EqualTo("BTCUSDT"));
    }

    [TestCase(TestName = "ExchangeClientBase: runtime parser может отправить ответ в активный сокет")]
    public async Task RuntimeParserCanSendResponseMessage()
    {
        using var client = new TestExchangeClient(enablePingReply: true);

        client.EnqueueTextMessage("ping:123");
        await client.SubscribeAsync(["BTCUSDT"]);

        await Task.Delay(100);

        Assert.That(client.SentMessages, Is.EqualTo(["sub:BTCUSDT", "pong:123"]));
    }

    [TestCase(TestName = "ExchangeClientBase: reconnect восстанавливает подписки")]
    public async Task ReconnectResubscribesAfterClose()
    {
        using var client = new TestExchangeClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Is.EqualTo(["sub:BTCUSDT,ETHUSDT", "sub:BTCUSDT,ETHUSDT"]));
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTCUSDT", "ETHUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: reconnect переигрывает multi-payload подписки")]
    public async Task ReconnectReplaysMultiPayloadSubscriptions()
    {
        using var client = new TestExchangeClient(usePerMarketCommands: true, reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.SentMessages, Is.EqualTo(["sub:BTCUSDT", "sub:ETHUSDT", "sub:BTCUSDT", "sub:ETHUSDT"]));
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTCUSDT", "ETHUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: reconnect не использует отменённый runtime token")]
    public async Task ReconnectUsesFreshTokenAfterStoppingPreviousRuntime()
    {
        using var client = new TestExchangeClient(reconnectDelay: TimeSpan.FromMilliseconds(10), failIfConnectTokenCancelled: true);
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(args.MarketIds, Is.EquivalentTo(["BTCUSDT"]));
    }

    [TestCase(TestName = "ExchangeClientBase: reconnect стабильно переживает несколько последовательных close frames")]
    public async Task ReconnectHandlesMultipleSequentialCloseFrames()
    {
        using var client = new TestExchangeClient(reconnectDelay: TimeSpan.FromMilliseconds(10), failIfConnectTokenCancelled: true);
        var reconnectCount = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            var currentCount = Interlocked.Increment(ref reconnectCount);
            if (currentCount < 3)
                client.EnqueueCloseMessage();
            else
                completed.TrySetResult();

            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT"]);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(reconnectCount, Is.EqualTo(3));
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(4));
        Assert.That(client.SentMessages.Count(message => message == "sub:BTCUSDT"), Is.EqualTo(4));
    }

    [TestCase(TestName = "ExchangeClientBase: connect может использовать async resolved endpoint")]
    public async Task ConnectUsesResolvedEndpoint()
    {
        var resolvedEndpoint = new Uri("wss://resolved.example.invalid/ws");
        using var client = new TestExchangeClient(resolvedEndpoint: resolvedEndpoint);

        await client.SubscribeAsync(["BTCUSDT"]);

        Assert.That(client.ConnectedEndpoints, Is.EqualTo([resolvedEndpoint]));
    }

    [TestCase(TestName = "MarketRealtimeUpdateExtensions: snapshot создаётся из runtime update")]
    public void RuntimeUpdateCreatesSnapshot()
    {
        var update = new MarketRealtimeUpdate("BTCUSDT", 100.5, 101.5, 101.0, 12345, MarketRealtimeUpdateKind.Ticker);

        var ok = update.TryCreateSnapshot(out var snapshot);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ok, Is.True);
        Assert.That(snapshot.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(snapshot.BestBid, Is.EqualTo(100.5));
        Assert.That(snapshot.BestAsk, Is.EqualTo(101.5));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(101.0));
    }

    [TestCase(TestName = "MarketRuntimePriceStreamBridge: записывает runtime update в writable stream")]
    public async Task RuntimeBridgeWritesToWritablePriceStream()
    {
        using var client = new TestExchangeClient();
        using var stream = new TestWritablePriceStream();
        using var bridge = new MarketRuntimePriceStreamBridge(client, stream);

        await client.InjectMessageAsync("update:BTCUSDT:100.5:101.5:101.0");

        var snapshot = stream.GetPrice("BTCUSDT");
        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.LastTradePrice, Is.EqualTo(101.0));
    }

    private sealed class TestExchangeClient(
        bool usePerMarketCommands = false,
        bool acceptBinaryMessages = false,
        bool enablePingReply = false,
        bool failIfConnectTokenCancelled = false,
        Uri? resolvedEndpoint = null,
        TimeSpan reconnectDelay = default)
        : ExchangeClientBase(reconnectDelay: reconnectDelay == default ? TimeSpan.FromMilliseconds(25) : reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private readonly bool usePerMarketCommands = usePerMarketCommands;
        private readonly bool acceptBinaryMessages = acceptBinaryMessages;
        private readonly bool enablePingReply = enablePingReply;
        private readonly Uri? resolvedEndpoint = resolvedEndpoint;
        private int processedMessages;
        private volatile bool connected;

        public override string PlatformName => "TestExchange";

        protected override Uri EndpointUri => new("wss://example.invalid/ws");

        public int ConnectCount { get; private set; }

        public List<string> SentMessages { get; } = [];

        public List<Uri> ConnectedEndpoints { get; } = [];

        public string[] Subscriptions => CurrentSubscriptions;

        public void EnqueueTextMessage(string text) =>
            receiveFrames.Enqueue(new ReceiveFrame(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true));

        public void EnqueueBinaryMessage(string text) =>
            receiveFrames.Enqueue(new ReceiveFrame(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Binary, true));

        public void EnqueueCloseMessage() =>
            receiveFrames.Enqueue(new ReceiveFrame([], WebSocketMessageType.Close, true));

        public ValueTask InjectMessageAsync(string text) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(text), CancellationToken.None);

        protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds) =>
            Encoding.UTF8.GetBytes($"sub:{string.Join(',', marketIds)}");

        protected override IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
        {
            if (!usePerMarketCommands)
            {
                yield return BuildSubscribeMessage(marketIds);
                yield break;
            }

            foreach (var marketId in marketIds)
                yield return Encoding.UTF8.GetBytes($"sub:{marketId}");
        }

        protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds) =>
            Encoding.UTF8.GetBytes($"unsub:{string.Join(',', marketIds)}");

        protected override IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
        {
            if (!usePerMarketCommands)
            {
                yield return BuildUnsubscribeMessage(marketIds);
                yield break;
            }

            foreach (var marketId in marketIds)
                yield return Encoding.UTF8.GetBytes($"unsub:{marketId}");
        }

        protected override bool IsSocketConnected(ClientWebSocket socket) => connected;

        protected override ValueTask<Uri> ResolveEndpointUriAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(resolvedEndpoint ?? EndpointUri);

        protected override ValueTask ConnectSocketAsync(ClientWebSocket socket, Uri endpointUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failIfConnectTokenCancelled)
                cancellationToken.ThrowIfCancellationRequested();

            ConnectCount++;
            connected = true;
            ConnectedEndpoints.Add(endpointUri);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask SendSocketMessageAsync(
            ClientWebSocket socket,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            SentMessages.Add(Encoding.UTF8.GetString(payload.Span));
            return ValueTask.CompletedTask;
        }

        protected override ValueTask SendSocketMessageAsync(
            ClientWebSocket socket,
            ReadOnlyMemory<byte> payload,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SentMessages.Add(Encoding.UTF8.GetString(payload.Span));
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask<ValueWebSocketReceiveResult> ReceiveSocketAsync(
            ClientWebSocket socket,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (receiveFrames.TryDequeue(out var frame))
                {
                    frame.Payload.CopyTo(buffer);
                    if (frame.MessageType == WebSocketMessageType.Close)
                        connected = false;

                    return new ValueWebSocketReceiveResult(frame.Payload.Length, frame.MessageType, frame.EndOfMessage);
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            throw new OperationCanceledException(cancellationToken);
        }

        protected override ValueTask CloseSocketAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            connected = false;
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<ReadOnlyMemory<byte>?> PrepareIncomingMessageAsync(
            ReadOnlyMemory<byte> payload,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            if (messageType != WebSocketMessageType.Text
                && !(acceptBinaryMessages && messageType == WebSocketMessageType.Binary))
            {
                return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
            }

            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(payload);
        }

        protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            var text = Encoding.UTF8.GetString(payload.Span);
            if (enablePingReply && text.StartsWith("ping:", StringComparison.Ordinal))
            {
                await SendRuntimeMessageAsync(Encoding.UTF8.GetBytes($"pong:{text[5..]}"), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!text.StartsWith("update:", StringComparison.Ordinal))
                return;

            var parts = text.Split(':');
            var update = new MarketRealtimeUpdate(
                parts[1],
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture),
                double.Parse(parts[4], CultureInfo.InvariantCulture),
                Environment.TickCount64,
                MarketRealtimeUpdateKind.Ticker);

            await PublishMarketUpdateAsync(update).ConfigureAwait(false);
            Interlocked.Increment(ref processedMessages);
        }

        private readonly record struct ReceiveFrame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage);
    }

    private sealed class TestWritablePriceStream : IWritableMarketPriceStream
    {
        private readonly Dictionary<string, IMarketPriceSnapshot> cache = new(StringComparer.OrdinalIgnoreCase);

        public int TokenCount => cache.Count;

        public IMarketPriceSnapshot? GetPrice(string assetId) =>
            cache.TryGetValue(assetId, out var snapshot) ? snapshot : null;

        public void SetPrice(string assetId, IMarketPriceSnapshot snapshot) =>
            cache[assetId] = snapshot;

        public void ClearCache() => cache.Clear();

        public void Dispose() => cache.Clear();
    }
}