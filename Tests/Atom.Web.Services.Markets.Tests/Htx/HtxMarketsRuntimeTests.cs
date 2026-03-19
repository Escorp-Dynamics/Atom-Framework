using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using Atom.Web.Services.Htx;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Htx.Tests;

/// <summary>
/// Runtime-тесты HtxClient после переноса на ExchangeClientBase.
/// </summary>
public class HtxMarketsRuntimeTests(ILogger logger) : BenchmarkTests<HtxMarketsRuntimeTests>(logger)
{
    public HtxMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "HTX runtime: subscribe payload строится по одному сообщению на символ")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableHtxClient();
        var payloads = client.BuildSubscribePayloads(["btcusdt", "ethusdt"])
            .Select(payload => Encoding.UTF8.GetString(payload.Span))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(2));
        Assert.That(payloads[0], Is.EqualTo("{\"sub\":\"market.btcusdt.ticker\",\"id\":\"btcusdt\"}"));
        Assert.That(payloads[1], Is.EqualTo("{\"sub\":\"market.ethusdt.ticker\",\"id\":\"ethusdt\"}"));
    }

    [TestCase(TestName = "HTX runtime: gunzip payload декодируется перед разбором")]
    public async Task GzipPayloadIsDecodedBeforeParsing()
    {
        var client = new TestableHtxClient();
        var decoded = await client.DecodeIncomingAsync(Compress("{" +
            "\"ch\":\"market.btcusdt.ticker\"," +
            "\"tick\":{\"bid\":65000.1,\"ask\":65000.2,\"lastPrice\":65000.15}" +
            "}"), WebSocketMessageType.Binary);

        Assert.That(Encoding.UTF8.GetString(decoded.Span), Does.Contain("market.btcusdt.ticker"));
    }

    [TestCase(TestName = "HTX runtime: ping payload отправляет pong")]
    public async Task PingPayloadSendsPong()
    {
        var client = new TestableRuntimeHtxClient();
        await client.SubscribeAsync(["btcusdt"]);
        await client.InjectPayloadAsync("{\"ping\":1492420473027}");

        Assert.That(client.SentMessages.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages[^1], Is.EqualTo("{\"pong\":1492420473027}"));
    }

    [TestCase(TestName = "HTX runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableHtxClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("{\"status\":\"ok\",\"subbed\":\"market.btcusdt.ticker\",\"id\":\"btcusdt\",\"ts\":1494326028889}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["btcusdt"]));
    }

    [TestCase(TestName = "HTX runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableHtxClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("{" +
            "\"ch\":\"market.btcusdt.ticker\"," +
            "\"ts\":1630982370526," +
            "\"tick\":{\"bid\":52732.88,\"ask\":52732.89,\"lastPrice\":52735.63}" +
            "}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("btcusdt"));
        Assert.That(received.Value.BestBid, Is.EqualTo(52732.88));
        Assert.That(received.Value.BestAsk, Is.EqualTo(52732.89));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(52735.63));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "HTX runtime: detail payload использует close как fallback last price")]
    public async Task DetailPayloadUsesCloseFallback()
    {
        var client = new TestableHtxClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("{" +
            "\"ch\":\"market.ethusdt.ticker\"," +
            "\"tick\":{\"close\":3123.45}" +
            "}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("ethusdt"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(3123.45));
        Assert.That(received.Value.BestBid, Is.Null);
        Assert.That(received.Value.BestAsk, Is.Null);
    }

    [TestCase(TestName = "HTX runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableHtxClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("{\"status\":\"error\",\"err-code\":\"bad-request\",\"err-msg\":\"invalid topic\"}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("HTX WebSocket error bad-request: invalid topic"));
    }

    [TestCase(TestName = "HTX runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableHtxClient();
        using var stream = new HtxPriceStream(client);

        await client.InjectPayloadAsync("{" +
            "\"ch\":\"market.btcusdt.ticker\"," +
            "\"tick\":{\"bid\":52732.88,\"ask\":52732.89,\"lastPrice\":52735.63}" +
            "}");

        var snapshot = stream.GetPrice("btcusdt");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(52732.88));
        Assert.That(snapshot.BestAsk, Is.EqualTo(52732.89));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(52735.63));
    }

    [TestCase(TestName = "HTX runtime: dispose price stream останавливает runtime bridge")]
    public async Task DisposedPriceStreamStopsRuntimeBridgeWrites()
    {
        var client = new TestableHtxClient();
        var stream = new HtxPriceStream(client);
        stream.Dispose();

        await client.InjectPayloadAsync("{" +
            "\"ch\":\"market.btcusdt.ticker\"," +
            "\"tick\":{\"bid\":52732.88,\"ask\":52732.89,\"lastPrice\":52735.63}" +
            "}");

        Assert.That(stream.GetPrice("btcusdt"), Is.Null);
    }

    [TestCase(TestName = "HTX runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        await using var client = new TestableRuntimeHtxClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["btcusdt"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages.Count(message => message.Contains("\"sub\":\"market.btcusdt.ticker\"", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(args.MarketIds, Is.EquivalentTo(["btcusdt"]));
    }

    private static byte[] Compress(string json)
    {
        using var compressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            gzipStream.Write(bytes, 0, bytes.Length);
        }

        return compressedStream.ToArray();
    }

    private sealed class TestableHtxClient : HtxClient
    {
        public IEnumerable<ReadOnlyMemory<byte>> BuildSubscribePayloads(string[] marketIds) =>
            BuildSubscribeMessages(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);

        public async ValueTask<ReadOnlyMemory<byte>> DecodeIncomingAsync(byte[] payload, WebSocketMessageType messageType)
        {
            var decoded = await PrepareIncomingMessageAsync(payload, messageType, CancellationToken.None);
            return decoded ?? ReadOnlyMemory<byte>.Empty;
        }
    }

    private sealed class TestableRuntimeHtxClient(TimeSpan reconnectDelay = default)
        : HtxClient(reconnectDelay: reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private volatile bool connected;

        public int ConnectCount { get; private set; }

        public List<string> SentMessages { get; } = [];

        public void EnqueueCloseMessage() =>
            receiveFrames.Enqueue(new ReceiveFrame([], WebSocketMessageType.Close, true));

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);

        protected override bool IsSocketConnected(ClientWebSocket socket) => connected;

        protected override ValueTask ConnectSocketAsync(ClientWebSocket socket, Uri endpointUri, CancellationToken cancellationToken)
        {
            connected = true;
            ConnectCount++;
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

        private readonly record struct ReceiveFrame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage);
    }
}