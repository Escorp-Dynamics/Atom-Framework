using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Kraken;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Kraken.Tests;

/// <summary>
/// Runtime-тесты KrakenClient после переноса на ExchangeClientBase.
/// </summary>
public class KrakenMarketsRuntimeTests(ILogger logger) : BenchmarkTests<KrakenMarketsRuntimeTests>(logger)
{
    public KrakenMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Kraken runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableKrakenClient();
        var payload = client.BuildSubscribePayload(["BTC/USD", "ETH/USD"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var symbols = root.GetProperty("params").GetProperty("symbol").EnumerateArray().Select(static item => item.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("subscribe"));
        Assert.That(root.GetProperty("params").GetProperty("channel").GetString(), Is.EqualTo("ticker"));
        Assert.That(symbols, Is.EqualTo(new[] { "BTC/USD", "ETH/USD" }));
    }

    [TestCase(TestName = "Kraken runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableKrakenClient();
        var payload = client.BuildUnsubscribePayload(["BTC/USD"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("params").GetProperty("symbol")[0].GetString(), Is.EqualTo("BTC/USD"));
    }

    [TestCase(TestName = "Kraken runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableKrakenClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "channel": "ticker",
              "type": "snapshot",
              "data": [
                {
                  "symbol": "BTC/USD",
                  "bid": 66500.10,
                  "ask": 66501.20,
                  "last": 66500.70
                }
              ]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC/USD"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Kraken runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableKrakenClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "channel": "ticker",
                            "type": "snapshot",
                            "data": [
                                {
                                    "symbol": "BTC/USD",
                                    "bid": "not-a-number",
                                    "ask": "still-not-a-number",
                                    "last": "also-not-a-number"
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Kraken runtime: unknown channel игнорируется без update")]
    public async Task UnknownChannelDoesNotPublishUpdate()
    {
        var client = new TestableKrakenClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "channel": "book",
                            "type": "snapshot",
                            "data": [
                                {
                                    "symbol": "BTC/USD",
                                    "bid": 66500.10,
                                    "ask": 66501.20,
                                    "last": 66500.70
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Kraken runtime: ticker array payload публикует update для каждого элемента")]
    public async Task TickerArrayPayloadPublishesUpdates()
    {
        var client = new TestableKrakenClient();
        var received = new List<MarketRealtimeUpdate>();

        client.MarketUpdateReceived += (sender, args) =>
        {
            received.Add(args.Update);
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "channel": "ticker",
                            "type": "snapshot",
                            "data": [
                                {
                                    "symbol": "BTC/USD",
                                    "bid": 66500.10,
                                    "ask": 66501.20,
                                    "last": 66500.70
                                },
                                {
                                    "symbol": "ETH/USD",
                                    "bid": 3200.10,
                                    "ask": 3200.90,
                                    "last": 3200.50
                                }
                            ]
                        }
                        """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(received, Has.Count.EqualTo(2));
        Assert.That(received.Select(static update => update.AssetId), Is.EquivalentTo(["BTC/USD", "ETH/USD"]));
    }

    [TestCase(TestName = "Kraken runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableKrakenClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "method": "subscribe",
              "success": true,
              "result": {
                "channel": "ticker",
                "symbol": ["BTC/USD"]
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC/USD"]));
    }

    [TestCase(TestName = "Kraken runtime: subscribe ack с params.symbol публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckWithParamsPublishesSubscriptionAcknowledged()
    {
        var client = new TestableKrakenClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "method": "subscribe",
              "success": true,
              "params": {
                "channel": "ticker",
                "symbol": ["BTC/USD"]
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC/USD"]));
    }

    [TestCase(TestName = "Kraken runtime: unsubscribe success игнорируется без ack и error")]
    public async Task UnsubscribeSuccessIsIgnored()
    {
        var client = new TestableKrakenClient();
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
              "method": "unsubscribe",
              "success": true,
              "result": {
                "channel": "ticker",
                "symbol": ["BTC/USD"]
              }
            }
            """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(acknowledgements, Is.EqualTo(0));
        Assert.That(runtimeErrors, Is.EqualTo(0));
    }

    [TestCase(TestName = "Kraken runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableKrakenClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "method": "subscribe",
              "success": false,
              "error": "Invalid symbol"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Kraken WebSocket subscribe: Invalid symbol"));
    }

    [TestCase(TestName = "Kraken runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableKrakenClient();
        using var stream = new KrakenPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "channel": "ticker",
              "type": "snapshot",
              "data": [
                {
                  "symbol": "ETH/USD",
                  "bid": 3200.10,
                  "ask": 3200.90,
                  "last": 3200.50
                }
              ]
            }
            """);

        var snapshot = stream.GetPrice("ETH/USD");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Kraken runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableKrakenClient();
        var stream = new KrakenPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "channel": "ticker",
                            "type": "snapshot",
                            "data": [
                                {
                                    "symbol": "SOL/USD",
                                    "bid": 150.10,
                                    "ask": 150.90,
                                    "last": 150.50
                                }
                            ]
                        }
                        """);

        Assert.That(stream.GetPrice("SOL/USD"), Is.Null);
    }

    [TestCase(TestName = "Kraken runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeKrakenClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC/USD"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"method\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC/USD"]));
    }

    private sealed class TestableKrakenClient : KrakenClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeKrakenClient(TimeSpan reconnectDelay = default) : KrakenClient(reconnectDelay: reconnectDelay)
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