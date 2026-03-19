using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Deribit;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Deribit.Tests;

/// <summary>
/// Runtime-тесты DeribitClient после переноса на ExchangeClientBase.
/// </summary>
public class DeribitMarketsRuntimeTests(ILogger logger) : BenchmarkTests<DeribitMarketsRuntimeTests>(logger)
{
    public DeribitMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Deribit runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableDeribitClient();
        var payload = client.BuildSubscribePayload(["BTC-PERPETUAL", "ETH-PERPETUAL"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;
        var channels = root.GetProperty("params").GetProperty("channels").EnumerateArray().Select(static item => item.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("public/subscribe"));
        Assert.That(root.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(channels, Is.EqualTo(new[] { "ticker.BTC-PERPETUAL.100ms", "ticker.ETH-PERPETUAL.100ms" }));
    }

    [TestCase(TestName = "Deribit runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableDeribitClient();
        var payload = client.BuildUnsubscribePayload(["BTC-PERPETUAL"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;

        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("public/unsubscribe"));
        Assert.That(root.GetProperty("params").GetProperty("channels")[0].GetString(), Is.EqualTo("ticker.BTC-PERPETUAL.100ms"));
    }

    [TestCase(TestName = "Deribit runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableDeribitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "method": "subscription",
              "params": {
                "channel": "ticker.BTC-PERPETUAL.100ms",
                "data": {
                  "instrument_name": "BTC-PERPETUAL",
                  "best_bid_price": 66500.10,
                  "best_ask_price": 66501.20,
                  "last_price": 66500.70
                }
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC-PERPETUAL"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Deribit runtime: invalid numerics игнорируются")]
    public async Task InvalidNumericsAreIgnored()
    {
        var client = new TestableDeribitClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "subscription",
                            "params": {
                                "channel": "ticker.BTC-PERPETUAL.100ms",
                                "data": {
                                    "instrument_name": "BTC-PERPETUAL",
                                    "best_bid_price": "bad",
                                    "best_ask_price": null,
                                    "last_price": false
                                }
                            }
                        }
                        """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: неизвестный subscription channel игнорируется")]
    public async Task UnknownSubscriptionChannelIsIgnored()
    {
        var client = new TestableDeribitClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "subscription",
                            "params": {
                                "channel": "book.BTC-PERPETUAL.100ms",
                                "data": {
                                    "instrument_name": "BTC-PERPETUAL",
                                    "best_bid_price": 66500.10,
                                    "best_ask_price": 66501.20,
                                    "last_price": 66500.70
                                }
                            }
                        }
                        """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: instrument_name fallback берётся из ticker channel")]
    public async Task TickerChannelFallbackPublishesAssetId()
    {
        var client = new TestableDeribitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "subscription",
                            "params": {
                                "channel": "ticker.ETH-PERPETUAL.100ms",
                                "data": {
                                    "best_bid_price": 3200.10,
                                    "best_ask_price": 3200.90,
                                    "last_price": 3200.50
                                }
                            }
                        }
                        """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("ETH-PERPETUAL"));
    }

    [TestCase(TestName = "Deribit runtime: asset id берётся из ticker channel даже при конфликтующем instrument_name")]
    public async Task TickerChannelTakesPrecedenceOverInstrumentName()
    {
        var client = new TestableDeribitClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "subscription",
                            "params": {
                                "channel": "ticker.BTC-PERPETUAL.100ms",
                                "data": {
                                    "instrument_name": "ETH-PERPETUAL",
                                    "best_bid_price": 66500.10,
                                    "best_ask_price": 66501.20,
                                    "last_price": 66500.70
                                }
                            }
                        }
                        """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC-PERPETUAL"));
    }

    [TestCase(TestName = "Deribit runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableDeribitClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "result": ["ticker.BTC-PERPETUAL.100ms"]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC-PERPETUAL"]));
    }

    [TestCase(TestName = "Deribit runtime: subscribe ack без ticker channels игнорируется")]
    public async Task SubscribeAckWithoutTickerChannelsIsIgnored()
    {
        var client = new TestableDeribitClient();
        var acknowledged = false;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            acknowledged = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "result": ["book.BTC-PERPETUAL.100ms"]
            }
            """);

        Assert.That(acknowledged, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: subscribe ack с non-array result игнорируется")]
    public async Task SubscribeAckWithNonArrayResultIsIgnored()
    {
        var client = new TestableDeribitClient();
        var acknowledged = false;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            acknowledged = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "result": {
                "channel": "ticker.BTC-PERPETUAL.100ms"
              }
            }
            """);

        Assert.That(acknowledged, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: unsubscribe ack не публикует subscribe acknowledgement")]
    public async Task UnsubscribeAckIsIgnored()
    {
        var client = new TestableDeribitClient();
        var acknowledged = false;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            acknowledged = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "id": 2,
                            "result": ["ticker.BTC-PERPETUAL.100ms"]
                        }
                        """);

        Assert.That(acknowledged, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: неизвестный method игнорируется")]
    public async Task UnknownMethodIsIgnored()
    {
        var client = new TestableDeribitClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "heartbeat",
                            "params": {
                                "channel": "ticker.BTC-PERPETUAL.100ms",
                                "data": {
                                    "instrument_name": "BTC-PERPETUAL",
                                    "best_bid_price": 66500.10,
                                    "best_ask_price": 66501.20,
                                    "last_price": 66500.70
                                }
                            }
                        }
                        """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Deribit runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableDeribitClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "error": {
                "code": 11050,
                "message": "bad_request"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Deribit WebSocket error 11050: bad_request"));
    }

    [TestCase(TestName = "Deribit runtime: error payload без message использует fallback текст")]
    public async Task ErrorPayloadWithoutMessageUsesFallbackText()
    {
        var client = new TestableDeribitClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "error": {
                "code": 11050
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Unknown Deribit runtime error."));
    }

    [TestCase(TestName = "Deribit runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableDeribitClient();
        using var stream = new DeribitPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "jsonrpc": "2.0",
              "method": "subscription",
              "params": {
                "channel": "ticker.ETH-PERPETUAL.100ms",
                "data": {
                  "instrument_name": "ETH-PERPETUAL",
                  "best_bid_price": 3200.10,
                  "best_ask_price": 3200.90,
                  "last_price": 3200.50
                }
              }
            }
            """);

        var snapshot = stream.GetPrice("ETH-PERPETUAL");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Deribit runtime: dispose price stream останавливает runtime bridge")]
    public async Task DisposedPriceStreamStopsRuntimeBridgeWrites()
    {
        var client = new TestableDeribitClient();
        var stream = new DeribitPriceStream(client);
        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "jsonrpc": "2.0",
                            "method": "subscription",
                            "params": {
                                "channel": "ticker.BTC-PERPETUAL.100ms",
                                "data": {
                                    "instrument_name": "BTC-PERPETUAL",
                                    "best_bid_price": 66500.10,
                                    "best_ask_price": 66501.20,
                                    "last_price": 66500.70
                                }
                            }
                        }
                        """);

        Assert.That(stream.GetPrice("BTC-PERPETUAL"), Is.Null);
    }

    [TestCase(TestName = "Deribit runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeDeribitClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC-PERPETUAL"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"method\":\"public/subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC-PERPETUAL"]));
    }

    private sealed class TestableDeribitClient : DeribitClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeDeribitClient(TimeSpan reconnectDelay = default) : DeribitClient(reconnectDelay: reconnectDelay)
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