using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Coinbase;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Coinbase.Tests;

/// <summary>
/// Runtime-тесты CoinbaseClient после переноса на ExchangeClientBase.
/// </summary>
public class CoinbaseMarketsRuntimeTests(ILogger logger) : BenchmarkTests<CoinbaseMarketsRuntimeTests>(logger)
{
    public CoinbaseMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Coinbase runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableCoinbaseClient();
        var payload = client.BuildSubscribePayload(["BTC-USD", "ETH-USD"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var products = root.GetProperty("product_ids").EnumerateArray().Select(x => x.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("subscribe"));
        Assert.That(root.GetProperty("channel").GetString(), Is.EqualTo("ticker"));
        Assert.That(products, Is.EqualTo(["BTC-USD", "ETH-USD"]));
    }

    [TestCase(TestName = "Coinbase runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableCoinbaseClient();
        var payload = client.BuildUnsubscribePayload(["BTC-USD"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("product_ids")[0].GetString(), Is.EqualTo("BTC-USD"));
    }

    [TestCase(TestName = "Coinbase runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableCoinbaseClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "channel": "ticker",
              "events": [
                {
                  "type": "update",
                  "tickers": [
                    {
                      "product_id": "BTC-USD",
                      "best_bid": "64500.10",
                      "best_ask": "64501.20",
                      "price": "64500.70"
                    }
                  ]
                }
              ]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC-USD"));
        Assert.That(received.Value.BestBid, Is.EqualTo(64500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(64501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(64500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Coinbase runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableCoinbaseClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "channel": "ticker",
                            "events": [
                                {
                                    "type": "update",
                                    "tickers": [
                                        {
                                            "product_id": "BTC-USD",
                                            "best_bid": "not-a-number",
                                            "best_ask": "still-not-a-number"
                                        }
                                    ]
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Coinbase runtime: unknown channel игнорируется без update")]
    public async Task UnknownChannelDoesNotPublishUpdate()
    {
        var client = new TestableCoinbaseClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "channel": "heartbeats",
                            "events": [
                                {
                                    "type": "update",
                                    "tickers": [
                                        {
                                            "product_id": "BTC-USD",
                                            "best_bid": "64500.10",
                                            "best_ask": "64501.20",
                                            "price": "64500.70"
                                        }
                                    ]
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Coinbase runtime: subscriptions payload публикует SubscriptionAcknowledged")]
    public async Task SubscriptionsPayloadPublishesSubscriptionAcknowledged()
    {
        var client = new TestableCoinbaseClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "subscriptions",
              "channels": [
                {
                  "name": "ticker",
                  "product_ids": ["BTC-USD", "ETH-USD"]
                }
              ]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC-USD", "ETH-USD"]));
        Assert.That(received.IsResubscription, Is.False);
    }

    [TestCase(TestName = "Coinbase runtime: subscriptions без ticker channel не публикуют ack")]
    public async Task NonTickerSubscriptionsDoNotPublishAcknowledged()
    {
        var client = new TestableCoinbaseClient();
        var acknowledgements = 0;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            Interlocked.Increment(ref acknowledgements);
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "type": "subscriptions",
                            "channels": [
                                {
                                    "name": "heartbeats",
                                    "product_ids": ["BTC-USD"]
                                }
                            ]
                        }
                        """);

        Assert.That(acknowledgements, Is.EqualTo(0));
    }

    [TestCase(TestName = "Coinbase runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableCoinbaseClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "error",
              "message": "subscription failed"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Is.EqualTo("subscription failed"));
    }

    [TestCase(TestName = "Coinbase runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableCoinbaseClient();
        using var stream = new CoinbasePriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "channel": "ticker",
              "events": [
                {
                  "type": "update",
                  "tickers": [
                    {
                      "product_id": "ETH-USD",
                      "best_bid": "3200.10",
                      "best_ask": "3200.90",
                      "price": "3200.50"
                    }
                  ]
                }
              ]
            }
            """);

        var snapshot = stream.GetPrice("ETH-USD");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Coinbase runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableCoinbaseClient();
        var stream = new CoinbasePriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "channel": "ticker",
                            "events": [
                                {
                                    "type": "update",
                                    "tickers": [
                                        {
                                            "product_id": "SOL-USD",
                                            "best_bid": "150.10",
                                            "best_ask": "150.90",
                                            "price": "150.50"
                                        }
                                    ]
                                }
                            ]
                        }
                        """);

        Assert.That(stream.GetPrice("SOL-USD"), Is.Null);
    }

    [TestCase(TestName = "Coinbase runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeCoinbaseClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC-USD"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"type\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC-USD"]));
    }

    private sealed class TestableCoinbaseClient : CoinbaseClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeCoinbaseClient(TimeSpan reconnectDelay = default) : CoinbaseClient(reconnectDelay: reconnectDelay)
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