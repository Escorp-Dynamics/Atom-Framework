using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.KuCoin;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.KuCoin.Tests;

/// <summary>
/// Runtime-тесты KuCoinClient после переноса на ExchangeClientBase.
/// </summary>
public class KuCoinMarketsRuntimeTests(ILogger logger) : BenchmarkTests<KuCoinMarketsRuntimeTests>(logger)
{
    public KuCoinMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "KuCoin runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableKuCoinClient();
        var payload = client.BuildSubscribePayload(["BTC-USDT", "ETH-USDT"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("subscribe"));
        Assert.That(root.GetProperty("topic").GetString(), Is.EqualTo("/market/ticker:BTC-USDT,ETH-USDT"));
        Assert.That(root.GetProperty("id").GetString(), Is.Not.Empty);
    }

    [TestCase(TestName = "KuCoin runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableKuCoinClient();
        var payload = client.BuildUnsubscribePayload(["BTC-USDT"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("topic").GetString(), Is.EqualTo("/market/ticker:BTC-USDT"));
        Assert.That(root.GetProperty("id").GetString(), Is.Not.Empty);
    }

    [TestCase(TestName = "KuCoin runtime: bootstrap endpoint использует token resolver")]
    public async Task BootstrapEndpointUsesResolvedToken()
    {
        using var client = new TestableRuntimeKuCoinClient();

        await client.SubscribeAsync(["BTC-USDT"]);

        Assert.That(client.ConnectedEndpoints, Has.Count.EqualTo(1));
        Assert.That(client.ConnectedEndpoints[0].ToString(), Does.Contain("token=test-token"));
    }

    [TestCase(TestName = "KuCoin runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableKuCoinClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "message",
              "topic": "/market/ticker:BTC-USDT",
              "data": {
                "bestBid": "66500.10",
                "bestAsk": "66501.20",
                "price": "66500.70"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTC-USDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(66500.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(66501.20));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "KuCoin runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableKuCoinClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "message",
              "topic": "/market/ticker:BTC-USDT",
              "data": {
                "bestBid": "bad",
                "bestAsk": "also-bad",
                "price": "still-bad"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: unknown topic игнорируется без update")]
    public async Task UnknownTopicDoesNotPublishUpdate()
    {
        var client = new TestableKuCoinClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "message",
              "topic": "/market/level2:BTC-USDT",
              "data": {
                "bestBid": "66500.10",
                "bestAsk": "66501.20",
                "price": "66500.70"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: unknown type игнорируется без update")]
    public async Task UnknownTypeDoesNotPublishUpdate()
    {
        var client = new TestableKuCoinClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "notice",
              "topic": "/market/ticker:BTC-USDT",
              "data": {
                "bestBid": "66500.10",
                "bestAsk": "66501.20",
                "price": "66500.70"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableKuCoinClient();
        MarketSubscriptionEventArgs? received = null;
        var subscribeId = client.ExtractRequestId(client.BuildSubscribePayload(["BTC-USDT"]));

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync($$"""
            {
              "id": "{{subscribeId}}",
              "type": "ack"
            }
            """
        );

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC-USDT"]));
    }

    [TestCase(TestName = "KuCoin runtime: unknown ack id не публикует SubscriptionAcknowledged")]
    public async Task UnknownAckIdDoesNotPublishSubscriptionAcknowledged()
    {
        var client = new TestableKuCoinClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": "unknown-request",
              "type": "ack"
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: unsubscribe ack не публикует SubscriptionAcknowledged")]
    public async Task UnsubscribeAckDoesNotPublishSubscriptionAcknowledged()
    {
        var client = new TestableKuCoinClient();
        MarketSubscriptionEventArgs? received = null;
        var unsubscribeId = client.ExtractRequestId(client.BuildUnsubscribePayload(["BTC-USDT"]));

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync($$"""
            {
              "id": "{{unsubscribeId}}",
              "type": "ack"
            }
            """
        );

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableKuCoinClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "error",
              "code": "400",
              "data": "invalid topic"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("invalid topic"));
    }

    [TestCase(TestName = "KuCoin runtime: error payload с msg публикует RuntimeError")]
    public async Task ErrorPayloadWithMsgPublishesRuntimeError()
    {
        var client = new TestableKuCoinClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "type": "error",
              "code": "401",
              "msg": "permission denied"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("permission denied"));
    }

    [TestCase(TestName = "KuCoin runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableKuCoinClient();
        using var stream = new KuCoinPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "type": "message",
              "topic": "/market/ticker:ETH-USDT",
              "data": {
                "bestBid": "3200.10",
                "bestAsk": "3200.90",
                "price": "3200.50"
              }
            }
            """);

        var snapshot = stream.GetPrice("ETH-USDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "KuCoin runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableKuCoinClient();
        var stream = new KuCoinPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
            {
              "type": "message",
              "topic": "/market/ticker:SOL-USDT",
              "data": {
                "bestBid": "150.10",
                "bestAsk": "150.90",
                "price": "150.50"
              }
            }
            """);

        Assert.That(stream.GetPrice("SOL-USDT"), Is.Null);
    }

    [TestCase(TestName = "KuCoin runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeKuCoinClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
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
        Assert.That(client.SentMessages.All(message => message.Contains("\"type\":\"subscribe\"", StringComparison.Ordinal)), Is.True);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC-USDT"]));
    }

    private sealed class TestableKuCoinClient : KuCoinClient
    {
        protected override ValueTask<Uri> ResolveEndpointUriAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Uri("wss://push1-v2.kucoin.test/endpoint?token=test-token&connectId=test-connect"));

        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public string ExtractRequestId(ReadOnlyMemory<byte> payload)
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            return document.RootElement.GetProperty("id").GetString()!;
        }

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeKuCoinClient(TimeSpan reconnectDelay = default)
        : KuCoinClient(reconnectDelay: reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private volatile bool connected;

        public int ConnectCount { get; private set; }

        public List<Uri> ConnectedEndpoints { get; } = [];

        public List<string> SentMessages { get; } = [];

        public void EnqueueCloseMessage() =>
            receiveFrames.Enqueue(new ReceiveFrame([], WebSocketMessageType.Close, true));

        protected override ValueTask<Uri> ResolveEndpointUriAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Uri("wss://push1-v2.kucoin.test/endpoint?token=test-token&connectId=test-connect"));

        protected override bool IsSocketConnected(ClientWebSocket socket) => connected;

        protected override ValueTask ConnectSocketAsync(ClientWebSocket socket, Uri endpointUri, CancellationToken cancellationToken)
        {
            connected = true;
            ConnectCount++;
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