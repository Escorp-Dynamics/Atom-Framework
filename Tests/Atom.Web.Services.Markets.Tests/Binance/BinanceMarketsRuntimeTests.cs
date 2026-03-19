using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Binance;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Binance.Tests;

/// <summary>
/// Runtime-тесты BinanceClient после переноса на ExchangeClientBase.
/// </summary>
public class BinanceMarketsRuntimeTests(ILogger logger) : BenchmarkTests<BinanceMarketsRuntimeTests>(logger)
{
    public BinanceMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Binance runtime: subscribe payload строится через базовый runtime")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableBinanceClient();
        var payload = client.BuildSubscribePayload(["BTCUSDT", "ETHUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;
        var parameters = root.GetProperty("params").EnumerateArray().Select(x => x.GetString()).ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("SUBSCRIBE"));
        Assert.That(parameters, Is.EqualTo(["btcusdt@bookTicker", "ethusdt@bookTicker"]));
    }

    [TestCase(TestName = "Binance runtime: unsubscribe payload строится через базовый runtime")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableBinanceClient();
        var payload = client.BuildUnsubscribePayload(["BTCUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("UNSUBSCRIBE"));
        Assert.That(root.GetProperty("params")[0].GetString(), Is.EqualTo("btcusdt@bookTicker"));
    }

    [TestCase(TestName = "Binance runtime: trade stream payload строится из BinanceStreamSelection")]
    public void TradeStreamPayloadGeneration()
    {
        var client = new TestableBinanceClient(new BinanceStreamSelection(BinanceStreamType.Trade));
        var payload = client.BuildSubscribePayload(["BTCUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("params")[0].GetString(), Is.EqualTo("btcusdt@trade"));
    }

    [TestCase(TestName = "Binance runtime: kline stream payload строится из BinanceStreamSelection")]
    public void KlineStreamPayloadGeneration()
    {
        var client = new TestableBinanceClient(new BinanceStreamSelection(BinanceStreamType.Kline, "5m"));
        var payload = client.BuildSubscribePayload(["ETHUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("params")[0].GetString(), Is.EqualTo("ethusdt@kline_5m"));
    }

    [TestCase(TestName = "Binance runtime: aggTrade stream payload строится из BinanceStreamSelection")]
    public void AggregateTradeStreamPayloadGeneration()
    {
        var client = new TestableBinanceClient(new BinanceStreamSelection(BinanceStreamType.AggregateTrade));
        var payload = client.BuildSubscribePayload(["BTCUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("params")[0].GetString(), Is.EqualTo("btcusdt@aggTrade"));
    }

    [TestCase(TestName = "Binance runtime: ticker stream payload строится из BinanceStreamSelection")]
    public void TickerStreamPayloadGeneration()
    {
        var client = new TestableBinanceClient(new BinanceStreamSelection(BinanceStreamType.TwentyFourHourTicker));
        var payload = client.BuildSubscribePayload(["ETHUSDT"]);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
        var root = document.RootElement;

        Assert.That(root.GetProperty("params")[0].GetString(), Is.EqualTo("ethusdt@ticker"));
    }

    [TestCase(TestName = "Binance runtime: bookTicker payload публикует MarketUpdateReceived")]
    public async Task BookTickerPayloadPublishesMarketUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "u": 400900217,
              "s": "BTCUSDT",
              "b": "64000.10",
              "a": "64000.90"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(64000.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(64000.90));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Binance runtime: combined stream envelope распаковывается в MarketUpdateReceived")]
    public async Task CombinedEnvelopePublishesMarketUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "stream": "btcusdt@bookTicker",
              "data": {
                "u": 400900217,
                "s": "BTCUSDT",
                "b": "64010.10",
                "a": "64010.90"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(64010.10));
        Assert.That(received.Value.BestAsk, Is.EqualTo(64010.90));
    }

    [TestCase(TestName = "Binance runtime: trade payload публикует Trade update")]
    public async Task TradePayloadPublishesTradeUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "e": "trade",
              "E": 1672515782136,
              "s": "BTCUSDT",
              "t": 12345,
              "p": "64100.25",
              "q": "0.010"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(64100.25));
        Assert.That(received.Value.BestBid, Is.Null);
        Assert.That(received.Value.BestAsk, Is.Null);
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Trade));
    }

    [TestCase(TestName = "Binance runtime: aggTrade payload публикует Trade update")]
    public async Task AggregateTradePayloadPublishesTradeUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "e": "aggTrade",
              "E": 1672515782136,
              "s": "ETHUSDT",
              "a": 5933014,
              "p": "3205.75",
              "q": "0.250"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("ETHUSDT"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(3205.75));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Trade));
    }

    [TestCase(TestName = "Binance runtime: kline payload публикует snapshot update по close price")]
    public async Task KlinePayloadPublishesSnapshotUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "e": "kline",
              "E": 1672515782136,
              "s": "BTCUSDT",
              "k": {
                "t": 1672515780000,
                "T": 1672515839999,
                "s": "BTCUSDT",
                "i": "1m",
                "o": "64050.00",
                "c": "64125.50",
                "h": "64130.00",
                "l": "64040.00"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(64125.50));
        Assert.That(received.Value.BestBid, Is.Null);
        Assert.That(received.Value.BestAsk, Is.Null);
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Binance runtime: 24hr ticker payload публикует ticker update")]
    public async Task TwentyFourHourTickerPayloadPublishesTickerUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "e": "24hrTicker",
              "E": 1672515782136,
              "s": "BTCUSDT",
              "p": "125.50",
              "c": "64180.75",
              "b": "64179.90",
              "a": "64181.10"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("BTCUSDT"));
        Assert.That(received.Value.BestBid, Is.EqualTo(64179.90));
        Assert.That(received.Value.BestAsk, Is.EqualTo(64181.10));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(64180.75));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "Binance runtime: invalid numeric fields не публикуют update")]
    public async Task InvalidNumericFieldsDoNotPublishUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "u": 400900217,
              "s": "BTCUSDT",
              "b": "not-a-number",
              "a": "still-not-a-number"
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Binance runtime: unknown event type игнорируется без update")]
    public async Task UnknownEventTypeDoesNotPublishUpdate()
    {
        var client = new TestableBinanceClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "e": "depthUpdate",
              "E": 1672515782136,
              "s": "BTCUSDT"
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Binance runtime: unknown ack id не публикует SubscriptionAcknowledged")]
    public async Task UnknownAckIdDoesNotPublishSubscriptionAcknowledged()
    {
        var client = new TestableBinanceClient();
        var acknowledgements = 0;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            Interlocked.Increment(ref acknowledgements);
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "result": null,
              "id": 999999
            }
            """);

        Assert.That(acknowledgements, Is.EqualTo(0));
    }

    [TestCase(TestName = "Binance runtime: protocol error payload публикует RuntimeError")]
    public async Task ProtocolErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableBinanceClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "code": 2,
              "msg": "Invalid request"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Binance WebSocket error 2: Invalid request"));
        Assert.That(received.DuringReconnect, Is.False);
    }

    [TestCase(TestName = "Binance runtime: protocol error внутри combined envelope публикует RuntimeError")]
    public async Task CombinedEnvelopeErrorPublishesRuntimeError()
    {
        var client = new TestableBinanceClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "stream": "btcusdt@bookTicker",
              "data": {
                "code": 7,
                "msg": "Bad combined payload"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("Binance WebSocket error 7: Bad combined payload"));
    }

    [TestCase(TestName = "Binance runtime: ack payload публикует SubscriptionAcknowledged")]
    public async Task AckPayloadPublishesSubscriptionAcknowledged()
    {
        using var client = new TestableRuntimeBinanceClient();
        var acknowledged = new TaskCompletionSource<MarketSubscriptionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            if (!args.IsResubscription)
                acknowledged.TrySetResult(args);

            return ValueTask.CompletedTask;
        };

        await client.SubscribeAsync(["BTCUSDT", "ETHUSDT"]);
        var requestId = ExtractRequestId(client.SentMessages[0]);

        client.EnqueueTextMessage($"{{\"result\":null,\"id\":{requestId}}}");

        var args = await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(args.IsResubscription, Is.False);
        Assert.That(args.MarketIds, Is.EquivalentTo(["BTCUSDT", "ETHUSDT"]));
        Assert.That(client.SentMessages, Has.Count.EqualTo(1));
        Assert.That(client.SentMessages[0], Does.Contain("\"method\":\"SUBSCRIBE\""));
    }

    [TestCase(TestName = "Binance runtime: unsubscribe ack не публикует SubscriptionAcknowledged")]
    public async Task UnsubscribeAckDoesNotPublishSubscriptionAcknowledged()
    {
        using var client = new TestableRuntimeBinanceClient();
        var acknowledgements = 0;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            Interlocked.Increment(ref acknowledgements);
            return ValueTask.CompletedTask;
        };

        await client.SubscribeAsync(["BTCUSDT"]);
        client.EnqueueTextMessage($"{{\"result\":null,\"id\":{ExtractRequestId(client.SentMessages[0])}}}");

        await Task.Delay(50);
        await client.UnsubscribeAsync(["BTCUSDT"]);

        client.EnqueueTextMessage($"{{\"result\":null,\"id\":{ExtractRequestId(client.SentMessages[1])}}}");

        await Task.Delay(50);

        Assert.That(acknowledgements, Is.EqualTo(1));
    }

    [TestCase(TestName = "Binance runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableBinanceClient();
        using var stream = new BinancePriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "u": 400900217,
              "s": "ETHUSDT",
              "b": "3200.10",
              "a": "3200.90",
              "c": "3200.50"
            }
            """);

        var snapshot = stream.GetPrice("ETHUSDT");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(3200.10));
        Assert.That(snapshot.BestAsk, Is.EqualTo(3200.90));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Binance runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableBinanceClient();
        var stream = new BinancePriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
            {
              "u": 400900217,
              "s": "SOLUSDT",
              "b": "150.10",
              "a": "150.90",
              "c": "150.50"
            }
            """);

        Assert.That(stream.GetPrice("SOLUSDT"), Is.Null);
    }

    [TestCase(TestName = "Binance runtime: reconnect восстанавливает подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeBinanceClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var acknowledged = new TaskCompletionSource<MarketSubscriptionEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            if (args.IsResubscription)
                acknowledged.TrySetResult(args);

            return ValueTask.CompletedTask;
        };

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["BTCUSDT"]);

        var ackArgs = await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var reconnectArgs = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(ackArgs.IsResubscription, Is.True);
        Assert.That(ackArgs.MarketIds, Is.EquivalentTo(["BTCUSDT"]));
        Assert.That(reconnectArgs.MarketIds, Is.EquivalentTo(["BTCUSDT"]));
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Has.Count.EqualTo(2));
        Assert.That(client.SentMessages.All(message => message.Contains("\"method\":\"SUBSCRIBE\"", StringComparison.Ordinal)), Is.True);
    }

    private sealed class TestableBinanceClient(BinanceStreamSelection? streamSelection = null) : BinanceClient(streamSelection)
    {
        public ReadOnlyMemory<byte> BuildSubscribePayload(string[] marketIds) =>
            BuildSubscribeMessage(marketIds);

        public ReadOnlyMemory<byte> BuildUnsubscribePayload(string[] marketIds) =>
            BuildUnsubscribeMessage(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private static int ExtractRequestId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("id").GetInt32();
    }

    private sealed class TestableRuntimeBinanceClient(TimeSpan reconnectDelay = default) : BinanceClient(reconnectDelay: reconnectDelay)
    {
        private readonly ConcurrentQueue<ReceiveFrame> receiveFrames = new();
        private volatile bool connected;

        public int ConnectCount { get; private set; }

        public List<string> SentMessages { get; } = [];

        public void EnqueueTextMessage(string text) =>
            receiveFrames.Enqueue(new ReceiveFrame(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true));

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