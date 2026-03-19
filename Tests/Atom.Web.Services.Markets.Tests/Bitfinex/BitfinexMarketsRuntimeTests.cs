using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Bitfinex;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitfinex.Tests;

/// <summary>
/// Runtime-тесты BitfinexClient после переноса на ExchangeClientBase.
/// </summary>
public class BitfinexMarketsRuntimeTests(ILogger logger) : BenchmarkTests<BitfinexMarketsRuntimeTests>(logger)
{
    public BitfinexMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Bitfinex runtime: subscribe payload строится как multi-message последовательность")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableBitfinexClient();
        var payloads = client.BuildSubscribePayloads(["tBTCUSD", "tETHUSD"])
            .Select(payload => JsonDocument.Parse(payload.ToArray()))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(2));
        Assert.That(payloads.Select(document => document.RootElement.GetProperty("event").GetString()),
            Is.EqualTo(new[] { "subscribe", "subscribe" }));
        Assert.That(payloads.Select(document => document.RootElement.GetProperty("symbol").GetString()),
            Is.EqualTo(new[] { "tBTCUSD", "tETHUSD" }));

        foreach (var document in payloads)
            document.Dispose();
    }

    [TestCase(TestName = "Bitfinex runtime: unsubscribe payload использует chanId из subscribe ack")]
    public async Task UnsubscribePayloadGenerationUsesChannelId()
    {
        var client = new TestableBitfinexClient();
        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        var payloads = client.BuildUnsubscribePayloads(["tBTCUSD"])
            .Select(payload => JsonDocument.Parse(payload.ToArray()))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(1));
        Assert.That(payloads[0].RootElement.GetProperty("event").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(payloads[0].RootElement.GetProperty("chanId").GetInt32(), Is.EqualTo(224555));

        foreach (var document in payloads)
            document.Dispose();
    }

    [TestCase(TestName = "Bitfinex runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableBitfinexClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["tBTCUSD"]));
    }

    [TestCase(TestName = "Bitfinex runtime: trading ticker payload публикует MarketUpdateReceived")]
    public async Task TradingTickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableBitfinexClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        await client.InjectPayloadAsync("""
            [224555, [7616.5, 31.89055171, 7617.5, 43.35811863, -550.8, -0.0674, 7617.1, 8314.71, 8257.8, 7500]]
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("tBTCUSD"));
        Assert.That(received.Value.BestBid, Is.EqualTo(7616.5));
        Assert.That(received.Value.BestAsk, Is.EqualTo(7617.5));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(7617.1));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Bitfinex runtime: funding ticker payload использует funding индексы")]
    public async Task FundingTickerPayloadUsesFundingIndices()
    {
        var client = new TestableBitfinexClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 232591,
              "symbol": "fUSD",
              "currency": "USD"
            }
            """);

        await client.InjectPayloadAsync("""
            [232591, [0.00031933, 0.0002401, 30, 3939629.61, 0.00019012, 2, 307776.15, -0.00005823, -0.2344, 0.00019016, 122156333.45, 0.00027397, 0.00000068, null, null, 3441851.73]]
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("fUSD"));
        Assert.That(received.Value.BestBid, Is.EqualTo(0.0002401));
        Assert.That(received.Value.BestAsk, Is.EqualTo(0.00019012));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(0.00019016));
    }

    [TestCase(TestName = "Bitfinex runtime: heartbeat payload игнорируется")]
    public async Task HeartbeatPayloadIsIgnored()
    {
        var client = new TestableBitfinexClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        await client.InjectPayloadAsync("""
            [224555, "hb"]
            """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Bitfinex runtime: invalid numerics игнорируются")]
    public async Task InvalidNumericsAreIgnored()
    {
        var client = new TestableBitfinexClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        await client.InjectPayloadAsync("""
            [224555, [false, 31.89055171, "bad", 43.35811863, -550.8, -0.0674, null, 8314.71, 8257.8, 7500]]
            """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Bitfinex runtime: неизвестный chanId игнорируется")]
    public async Task UnknownChannelIdIsIgnored()
    {
        var client = new TestableBitfinexClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            [999999, [7616.5, 31.89055171, 7617.5, 43.35811863, -550.8, -0.0674, 7617.1, 8314.71, 8257.8, 7500]]
            """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Bitfinex runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableBitfinexClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "error",
              "code": 10300,
              "msg": "subscription failed"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Bitfinex WebSocket error 10300: subscription failed"));
    }

    [TestCase(TestName = "Bitfinex runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableBitfinexClient();
        using var stream = new BitfinexPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        await client.InjectPayloadAsync("""
            [224555, [7616.5, 31.89055171, 7617.5, 43.35811863, -550.8, -0.0674, 7617.1, 8314.71, 8257.8, 7500]]
            """);

        var snapshot = stream.GetPrice("tBTCUSD");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(7616.5));
        Assert.That(snapshot.BestAsk, Is.EqualTo(7617.5));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(7617.1));
    }

    [TestCase(TestName = "Bitfinex runtime: dispose price stream останавливает runtime bridge")]
    public async Task DisposedPriceStreamStopsRuntimeBridgeWrites()
    {
        var client = new TestableBitfinexClient();
        var stream = new BitfinexPriceStream(client);
        stream.Dispose();

        await client.InjectPayloadAsync("""
            {
              "event": "subscribed",
              "channel": "ticker",
              "chanId": 224555,
              "symbol": "tBTCUSD",
              "pair": "BTCUSD"
            }
            """);

        await client.InjectPayloadAsync("""
            [224555, [7616.5, 31.89055171, 7617.5, 43.35811863, -550.8, -0.0674, 7617.1, 8314.71, 8257.8, 7500]]
            """);

        Assert.That(stream.GetPrice("tBTCUSD"), Is.Null);
    }

    [TestCase(TestName = "Bitfinex runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        await using var client = new TestableRuntimeBitfinexClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["tBTCUSD"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages.Count(message => message.Contains("\"event\":\"subscribe\"", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(args.MarketIds, Is.EquivalentTo(["tBTCUSD"]));
    }

    private sealed class TestableBitfinexClient : BitfinexClient
    {
        public IEnumerable<ReadOnlyMemory<byte>> BuildSubscribePayloads(string[] marketIds) =>
            BuildSubscribeMessages(marketIds);

        public IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribePayloads(string[] marketIds) =>
            BuildUnsubscribeMessages(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeBitfinexClient(TimeSpan reconnectDelay = default)
        : BitfinexClient(reconnectDelay: reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private volatile bool connected;

        public int ConnectCount { get; private set; }

        public List<string> SentMessages { get; } = [];

        public void EnqueueCloseMessage() =>
            receiveFrames.Enqueue(new ReceiveFrame([], WebSocketMessageType.Close, true));

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