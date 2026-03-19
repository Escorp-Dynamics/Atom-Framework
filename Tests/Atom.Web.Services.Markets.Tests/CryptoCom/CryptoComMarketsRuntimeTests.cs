using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.CryptoCom;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.CryptoCom.Tests;

/// <summary>
/// Runtime-тесты CryptoComClient после переноса на ExchangeClientBase.
/// </summary>
public class CryptoComMarketsRuntimeTests(ILogger logger) : BenchmarkTests<CryptoComMarketsRuntimeTests>(logger)
{
    public CryptoComMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Crypto.com runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableCryptoComClient();
        var payload = client.BuildSubscribePayload(["BTC_USDT", "ETH_USDT"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;
        var channels = root.GetProperty("params").GetProperty("channels").EnumerateArray().Select(static item => item.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("subscribe"));
        Assert.That(channels, Is.EqualTo(new[] { "ticker.BTC_USDT", "ticker.ETH_USDT" }));
        Assert.That(root.TryGetProperty("nonce", out var nonceProperty), Is.True);
        Assert.That(nonceProperty.GetInt64(), Is.GreaterThan(0));
    }

    [TestCase(TestName = "Crypto.com runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableCryptoComClient();
        var payload = client.BuildUnsubscribePayload(["BTC_USDT"]);

        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("id").GetInt32(), Is.EqualTo(2));
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("unsubscribe"));
        Assert.That(root.GetProperty("params").GetProperty("channels")[0].GetString(), Is.EqualTo("ticker.BTC_USDT"));
        Assert.That(root.TryGetProperty("nonce", out var nonceProperty), Is.True);
        Assert.That(nonceProperty.GetInt64(), Is.GreaterThan(0));
    }

    [TestCase(TestName = "Crypto.com runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableCryptoComClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": 1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "BTC_USDT",
                "subscription": "ticker.BTC_USDT",
                "channel": "ticker"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["BTC_USDT"]));
    }

    [TestCase(TestName = "Crypto.com runtime: subscribe ack использует subscription fallback без instrument_name")]
    public async Task SubscribeAckUsesSubscriptionFallback()
    {
        var client = new TestableCryptoComClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": 1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "subscription": "ticker.ETH_USDT",
                "channel": "ticker"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["ETH_USDT"]));
    }

    [TestCase(TestName = "Crypto.com runtime: ticker payload публикует MarketUpdateReceived")]
    public async Task TickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableCryptoComClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "BTC_USDT",
                "subscription": "ticker.BTC_USDT",
                "channel": "ticker",
                "data": [
                  {
                    "a": "66500.70",
                    "b": "66500.10",
                    "k": "66501.20",
                    "i": "BTC_USDT",
                    "t": 1710000000000
                  }
                ]
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

    [TestCase(TestName = "Crypto.com runtime: ticker payload использует data instrument fallback")]
    public async Task TickerPayloadUsesDataInstrumentFallback()
    {
        var client = new TestableCryptoComClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "subscription": "ticker.SOL_USDT",
                "channel": "ticker",
                "data": [
                  {
                    "a": "150.50",
                    "b": "150.10",
                    "k": "150.90",
                    "i": "SOL_USDT"
                  }
                ]
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("SOL_USDT"));
    }

    [TestCase(TestName = "Crypto.com runtime: invalid numerics игнорируются")]
    public async Task InvalidNumericsAreIgnored()
    {
        var client = new TestableCryptoComClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "BTC_USDT",
                "subscription": "ticker.BTC_USDT",
                "channel": "ticker",
                "data": [
                  {
                    "a": false,
                    "b": "bad",
                    "k": null
                  }
                ]
              }
            }
            """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Crypto.com runtime: неизвестный channel игнорируется")]
    public async Task UnknownChannelIsIgnored()
    {
        var client = new TestableCryptoComClient();
        var published = false;

        client.MarketUpdateReceived += (sender, args) =>
        {
            published = true;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "BTC_USDT",
                "subscription": "trade.BTC_USDT",
                "channel": "trade",
                "data": [
                  {
                    "p": "66500.70"
                  }
                ]
              }
            }
            """);

        Assert.That(published, Is.False);
    }

    [TestCase(TestName = "Crypto.com runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableCryptoComClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "id": 1,
              "method": "subscribe",
              "code": 40004,
              "message": "invalid channel"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Crypto.com WebSocket error 40004: invalid channel"));
    }

    [TestCase(TestName = "Crypto.com runtime: heartbeat отвечает public/respond-heartbeat с тем же id")]
    public async Task HeartbeatRespondsWithMatchingId()
    {
        await using var client = new TestableRuntimeCryptoComClient();
        client.EnqueueTextMessage("""
            {
              "id": 1587523073344,
              "method": "public/heartbeat",
              "code": 0
            }
            """);

        await client.SubscribeAsync(["BTC_USDT"]);

        var heartbeatReply = await client.HeartbeatResponse.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var document = JsonDocument.Parse(heartbeatReply);
        var root = document.RootElement;

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("id").GetInt64(), Is.EqualTo(1587523073344));
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("public/respond-heartbeat"));
    }

    [TestCase(TestName = "Crypto.com runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableCryptoComClient();
        using var stream = new CryptoComPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "ETH_USDT",
                "subscription": "ticker.ETH_USDT",
                "channel": "ticker",
                "data": [
                  {
                    "a": "3200.50",
                    "b": "3200.10",
                    "k": "3200.90",
                    "i": "ETH_USDT"
                  }
                ]
              }
            }
            """);

        var snapshot = stream.GetPrice("ETH_USDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Crypto.com runtime: dispose price stream останавливает runtime bridge")]
    public async Task DisposedPriceStreamStopsRuntimeBridgeWrites()
    {
        var client = new TestableCryptoComClient();
        var stream = new CryptoComPriceStream(client);
        stream.Dispose();

        await client.InjectPayloadAsync("""
            {
              "id": -1,
              "method": "subscribe",
              "code": 0,
              "result": {
                "instrument_name": "BTC_USDT",
                "subscription": "ticker.BTC_USDT",
                "channel": "ticker",
                "data": [
                  {
                    "a": "66500.70",
                    "b": "66500.10",
                    "k": "66501.20",
                    "i": "BTC_USDT"
                  }
                ]
              }
            }
            """);

        Assert.That(stream.GetPrice("BTC_USDT"), Is.Null);
    }

    [TestCase(TestName = "Crypto.com runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        await using var client = new TestableRuntimeCryptoComClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTC_USDT"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages.Count(static message => message.Contains("\"method\":\"subscribe\"", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTC_USDT"]));
    }

    private sealed class TestableCryptoComClient : CryptoComClient
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeCryptoComClient(TimeSpan reconnectDelay = default)
        : CryptoComClient(reconnectDelay: reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private volatile bool connected;

        public int ConnectCount { get; private set; }

        public List<string> SentMessages { get; } = [];

        public TaskCompletionSource<string> HeartbeatResponse { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void EnqueueTextMessage(string json) =>
            receiveFrames.Enqueue(new ReceiveFrame(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true));

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
            var message = Encoding.UTF8.GetString(payload.Span);
            SentMessages.Add(message);

            if (message.Contains("\"method\":\"public/respond-heartbeat\"", StringComparison.Ordinal))
                HeartbeatResponse.TrySetResult(message);

            return ValueTask.CompletedTask;
        }

        protected override ValueTask SendSocketMessageAsync(
          ClientWebSocket socket,
          ReadOnlyMemory<byte> payload,
          WebSocketMessageType messageType,
          bool endOfMessage,
          CancellationToken cancellationToken) =>
          SendSocketMessageAsync(socket, payload, cancellationToken);

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