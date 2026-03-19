using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.GateIo;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.GateIo.Tests;

/// <summary>
/// Runtime-тесты GateIoClient после переноса на ExchangeClientBase.
/// </summary>
public class GateIoMarketsRuntimeTests(ILogger logger) : BenchmarkTests<GateIoMarketsRuntimeTests>(logger)
{
    public GateIoMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Gate.io runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableGateIoClient();
        var payload = client.BuildSubscribePayload(["BTC_USDT", "ETH_USDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var pairs = root.GetProperty("payload").EnumerateArray().Select(static element => element.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("channel").GetString(), Is.EqualTo("spot.tickers"));
        Assert.That(root.GetProperty("event").GetString(), Is.EqualTo("subscribe"));
        Assert.That(pairs, Is.EqualTo(new[] { "BTC_USDT", "ETH_USDT" }));
    }

    [TestCase(TestName = "Gate.io runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableGateIoClient();
        var payload = client.BuildUnsubscribePayload(["BTC_USDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("event").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("payload")[0].GetString(), Is.EqualTo("BTC_USDT"));
    }

    [TestCase(TestName = "Gate.io runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableGateIoClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "time": 1700000000,
              "channel": "spot.tickers",
              "event": "update",
              "result": {
                "currency_pair": "BTC_USDT",
                "last": "66500.70",
                "lowest_ask": "66501.20",
                "highest_bid": "66500.10"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC_USDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Gate.io runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableGateIoClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "time": 1700000000,
                            "channel": "spot.tickers",
                            "event": "update",
                            "result": {
                                "currency_pair": "BTC_USDT",
                                "last": "not-a-number",
                                "lowest_ask": "still-not-a-number",
                                "highest_bid": "also-not-a-number"
                            }
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Gate.io runtime: unknown channel игнорируется без update")]
    public async Task UnknownChannelDoesNotPublishUpdate()
    {
        var client = new TestableGateIoClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "time": 1700000000,
                            "channel": "spot.order_book",
                            "event": "update",
                            "result": {
                                "currency_pair": "BTC_USDT",
                                "last": "66500.70",
                                "lowest_ask": "66501.20",
                                "highest_bid": "66500.10"
                            }
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Gate.io runtime: ticker array payload публикует update для каждого элемента")]
    public async Task TickerArrayPayloadPublishesUpdates()
    {
        var client = new TestableGateIoClient();
        var received = new List<MarketRealtimeUpdate>();

        client.MarketUpdateReceived += (sender, args) =>
        {
            received.Add(args.Update);
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "time": 1700000000,
                            "channel": "spot.tickers",
                            "event": "update",
                            "result": [
                                {
                                    "currency_pair": "BTC_USDT",
                                    "last": "66500.70",
                                    "lowest_ask": "66501.20",
                                    "highest_bid": "66500.10"
                                },
                                {
                                    "currency_pair": "ETH_USDT",
                                    "last": "3200.50",
                                    "lowest_ask": "3200.90",
                                    "highest_bid": "3200.10"
                                }
                            ]
                        }
                        """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(received, Has.Count.EqualTo(2));
        Assert.That(received.Select(static update => update.AssetId), Is.EquivalentTo(["BTC_USDT", "ETH_USDT"]));
    }

    [TestCase(TestName = "Gate.io runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableGateIoClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "time": 1700000000,
              "channel": "spot.tickers",
              "event": "subscribe",
              "payload": ["BTC_USDT"]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC_USDT"]));
    }

    [TestCase(TestName = "Gate.io runtime: subscribe ack с result.currency_pair публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckWithResultPublishesSubscriptionAcknowledged()
    {
        var client = new TestableGateIoClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "time": 1700000000,
              "channel": "spot.tickers",
              "event": "subscribe",
              "result": {
                "status": "success",
                "currency_pair": "BTC_USDT"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC_USDT"]));
    }

    [TestCase(TestName = "Gate.io runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableGateIoClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "channel": "spot.tickers",
              "event": "error",
              "error": {
                "code": "4",
                "message": "invalid channel"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Gate.io WebSocket error 4: invalid channel"));
    }

    [TestCase(TestName = "Gate.io runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableGateIoClient();
        using var stream = new GateIoPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "time": 1700000000,
              "channel": "spot.tickers",
              "event": "update",
              "result": {
                "currency_pair": "ETH_USDT",
                "last": "3200.50",
                "lowest_ask": "3200.90",
                "highest_bid": "3200.10"
              }
            }
            """);

        var snapshot = stream.GetPrice("ETH_USDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Gate.io runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableGateIoClient();
        var stream = new GateIoPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "time": 1700000000,
                            "channel": "spot.tickers",
                            "event": "update",
                            "result": {
                                "currency_pair": "SOL_USDT",
                                "last": "150.50",
                                "lowest_ask": "150.90",
                                "highest_bid": "150.10"
                            }
                        }
                        """);

        Assert.That(stream.GetPrice("SOL_USDT"), Is.Null);
    }

    [TestCase(TestName = "Gate.io runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeGateIoClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC_USDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"event\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC_USDT"]));
    }

    private sealed class TestableGateIoClient : GateIoClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeGateIoClient(TimeSpan reconnectDelay = default) : GateIoClient(reconnectDelay: reconnectDelay)
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