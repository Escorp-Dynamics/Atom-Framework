using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Markets;
using Atom.Web.Services.Okx;

namespace Atom.Web.Services.Okx.Tests;

/// <summary>
/// Runtime-тесты OkxClient после переноса на ExchangeClientBase.
/// </summary>
public class OkxMarketsRuntimeTests(ILogger logger) : BenchmarkTests<OkxMarketsRuntimeTests>(logger)
{
    public OkxMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "OKX runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableOkxClient();
        var payload = client.BuildSubscribePayload(["BTC-USDT", "ETH-USDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var args = root.GetProperty("args").EnumerateArray().ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("op").GetString(), Is.EqualTo("subscribe"));
        Assert.That(args[0].GetProperty("channel").GetString(), Is.EqualTo("tickers"));
        Assert.That(args[0].GetProperty("instId").GetString(), Is.EqualTo("BTC-USDT"));
        Assert.That(args[1].GetProperty("instId").GetString(), Is.EqualTo("ETH-USDT"));
    }

    [TestCase(TestName = "OKX runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableOkxClient();
        var payload = client.BuildUnsubscribePayload(["BTC-USDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("op").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("args")[0].GetProperty("instId").GetString(), Is.EqualTo("BTC-USDT"));
    }

    [TestCase(TestName = "OKX runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableOkxClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "arg": {
                "channel": "tickers",
                "instId": "BTC-USDT"
              },
              "data": [
                {
                  "instId": "BTC-USDT",
                  "bidPx": "66500.10",
                  "askPx": "66501.20",
                  "last": "66500.70",
                  "ts": "1597026383085"
                }
              ]
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC-USDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "OKX runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableOkxClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "arg": {
                                "channel": "tickers",
                                "instId": "BTC-USDT"
                            },
                            "data": [
                                {
                                    "instId": "BTC-USDT",
                                    "bidPx": "not-a-number",
                                    "askPx": "still-not-a-number",
                                    "last": "also-not-a-number"
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "OKX runtime: unknown channel игнорируется без update")]
    public async Task UnknownChannelDoesNotPublishUpdate()
    {
        var client = new TestableOkxClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
                        {
                            "arg": {
                                "channel": "books",
                                "instId": "BTC-USDT"
                            },
                            "data": [
                                {
                                    "instId": "BTC-USDT",
                                    "bidPx": "66500.10",
                                    "askPx": "66501.20",
                                    "last": "66500.70"
                                }
                            ]
                        }
                        """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "OKX runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableOkxClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "subscribe",
              "arg": {
                "channel": "tickers",
                "instId": "BTC-USDT"
              },
              "connId": "accb8e21"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC-USDT"]));
    }

    [TestCase(TestName = "OKX runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableOkxClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "error",
              "code": "60012",
              "msg": "Invalid request"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("OKX WebSocket error 60012: Invalid request"));
    }

    [TestCase(TestName = "OKX runtime: notice payload публикует RuntimeError")]
    public async Task NoticePayloadPublishesRuntimeError()
    {
        var client = new TestableOkxClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "notice",
              "code": "64008",
              "msg": "The connection will soon be closed for a service upgrade"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("OKX WebSocket notice 64008"));
    }

    [TestCase(TestName = "OKX runtime: channel-conn-count-error публикует RuntimeError")]
    public async Task ChannelConnCountErrorPublishesRuntimeError()
    {
        var client = new TestableOkxClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "channel-conn-count-error",
              "code": "60030",
              "msg": "The connection count exceeds the limit"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("OKX WebSocket channel-conn-count-error 60030"));
    }

    [TestCase(TestName = "OKX runtime: unsubscribe event игнорируется без ack и error")]
    public async Task UnsubscribeEventIsIgnored()
    {
        var client = new TestableOkxClient();
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
              "event": "unsubscribe",
              "arg": {
                "channel": "tickers",
                "instId": "BTC-USDT"
              }
            }
            """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(acknowledgements, Is.EqualTo(0));
        Assert.That(runtimeErrors, Is.EqualTo(0));
    }

    [TestCase(TestName = "OKX runtime: channel-conn-count event игнорируется без ack и error")]
    public async Task ChannelConnCountEventIsIgnored()
    {
        var client = new TestableOkxClient();
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
              "event": "channel-conn-count",
              "channel": "tickers",
              "connCount": "1",
              "connId": "accb8e21"
            }
            """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(acknowledgements, Is.EqualTo(0));
        Assert.That(runtimeErrors, Is.EqualTo(0));
    }

    [TestCase(TestName = "OKX runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableOkxClient();
        using var stream = new OkxPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "arg": {
                "channel": "tickers",
                "instId": "ETH-USDT"
              },
              "data": [
                {
                  "instId": "ETH-USDT",
                  "bidPx": "3200.10",
                  "askPx": "3200.90",
                  "last": "3200.50",
                  "ts": "1597026383085"
                }
              ]
            }
            """);

        var snapshot = stream.GetPrice("ETH-USDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "OKX runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableOkxClient();
        var stream = new OkxPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
                        {
                            "arg": {
                                "channel": "tickers",
                                "instId": "SOL-USDT"
                            },
                            "data": [
                                {
                                    "instId": "SOL-USDT",
                                    "bidPx": "150.10",
                                    "askPx": "150.90",
                                    "last": "150.50"
                                }
                            ]
                        }
                        """);

        Assert.That(stream.GetPrice("SOL-USDT"), Is.Null);
    }

    [TestCase(TestName = "OKX runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeOkxClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC-USDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"op\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC-USDT"]));
    }

    private sealed class TestableOkxClient : OkxClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeOkxClient(TimeSpan reconnectDelay = default) : OkxClient(reconnectDelay: reconnectDelay)
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