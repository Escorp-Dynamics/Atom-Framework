using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Bybit;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bybit.Tests;

/// <summary>
/// Runtime-тесты BybitClient после переноса на ExchangeClientBase.
/// </summary>
public class BybitMarketsRuntimeTests(ILogger logger) : BenchmarkTests<BybitMarketsRuntimeTests>(logger)
{
    public BybitMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Bybit runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableBybitClient();
        var payload = client.BuildSubscribePayload(["BTCUSDT", "ETHUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var args = root.GetProperty("args").EnumerateArray().Select(static item => item.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("op").GetString(), Is.EqualTo("subscribe"));
        Assert.That(args, Is.EqualTo(new[] { "tickers.BTCUSDT", "tickers.ETHUSDT" }));
    }

    [TestCase(TestName = "Bybit runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableBybitClient();
        var payload = client.BuildUnsubscribePayload(["BTCUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("op").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("args")[0].GetString(), Is.EqualTo("tickers.BTCUSDT"));
    }

    [TestCase(TestName = "Bybit runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableBybitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "topic": "tickers.BTCUSDT",
              "type": "snapshot",
              "data": {
                "symbol": "BTCUSDT",
                "bid1Price": "66500.10",
                "ask1Price": "66501.20",
                "lastPrice": "66500.70"
              },
              "ts": 1700000000
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Bybit runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableBybitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "topic": "tickers.BTCUSDT",
                            "type": "snapshot",
                            "data": {
                                "symbol": "BTCUSDT",
                                "bid1Price": "not-a-number",
                                "ask1Price": "still-not-a-number",
                                "lastPrice": "also-not-a-number"
                            },
                            "ts": 1700000000
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bybit runtime: unknown topic игнорируется без update")]
    public async Task UnknownTopicDoesNotPublishUpdate()
    {
        var client = new TestableBybitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "topic": "orderbook.1.BTCUSDT",
                            "type": "snapshot",
                            "data": {
                                "symbol": "BTCUSDT",
                                "bid1Price": "66500.10",
                                "ask1Price": "66501.20",
                                "lastPrice": "66500.70"
                            },
                            "ts": 1700000000
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bybit runtime: symbol fallback берётся из topic")]
    public async Task SymbolFallsBackToTopic()
    {
        var client = new TestableBybitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "topic": "tickers.BTCUSDT",
                            "type": "snapshot",
                            "data": {
                                "bid1Price": "66500.10",
                                "ask1Price": "66501.20",
                                "lastPrice": "66500.70"
                            },
                            "ts": 1700000000
                        }
                        """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
    }

    [TestCase(TestName = "Bybit runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableBybitClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "success": true,
              "retMsg": "subscribe",
              "op": "subscribe",
              "args": ["tickers.BTCUSDT"]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTCUSDT"]));
    }

    [TestCase(TestName = "Bybit runtime: unsubscribe success игнорируется без ack и error")]
    public async Task UnsubscribeSuccessIsIgnored()
    {
        var client = new TestableBybitClient();
        var acknowledgements = 0;
        var runtimeErrors = 0;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            Interlocked.Increment(ref acknowledgements);
            return ValueTask.CompletedTask;
        };

        client.RuntimeError += (sender, args) =>
        {
            Interlocked.Increment(ref runtimeErrors);
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "success": true,
              "retMsg": "unsubscribe",
              "op": "unsubscribe",
              "args": ["tickers.BTCUSDT"]
            }
            """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(acknowledgements, Is.EqualTo(0));
        Assert.That(runtimeErrors, Is.EqualTo(0));
    }

    [TestCase(TestName = "Bybit runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableBybitClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "success": false,
              "retMsg": "handler not found",
              "retCode": 10404,
              "op": "subscribe",
              "args": ["tickers.BTCUSDT"]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Bybit WebSocket subscribe 10404: handler not found"));
    }

    [TestCase(TestName = "Bybit runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableBybitClient();
        using var stream = new BybitPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "topic": "tickers.ETHUSDT",
              "type": "snapshot",
              "data": {
                "symbol": "ETHUSDT",
                "bid1Price": "3200.10",
                "ask1Price": "3200.90",
                "lastPrice": "3200.50"
              },
              "ts": 1700000000
            }
            """);

        var snapshot = stream.GetPrice("ETHUSDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Bybit runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableBybitClient();
        var stream = new BybitPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "topic": "tickers.SOLUSDT",
                            "type": "snapshot",
                            "data": {
                                "symbol": "SOLUSDT",
                                "bid1Price": "150.10",
                                "ask1Price": "150.90",
                                "lastPrice": "150.50"
                            },
                            "ts": 1700000000
                        }
                        """);

        Assert.That(stream.GetPrice("SOLUSDT"), Is.Null);
    }

    [TestCase(TestName = "Bybit runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeBybitClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"op\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTCUSDT"]));
    }

    private sealed class TestableBybitClient : BybitClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeBybitClient(TimeSpan reconnectDelay = default) : BybitClient(reconnectDelay: reconnectDelay)
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