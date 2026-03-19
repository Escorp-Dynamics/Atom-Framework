using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Atom.Web.Services.Bitstamp;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitstamp.Tests;

/// <summary>
/// Runtime-тесты BitstampClient после переноса на ExchangeClientBase.
/// </summary>
public class BitstampMarketsRuntimeTests(ILogger logger) : BenchmarkTests<BitstampMarketsRuntimeTests>(logger)
{
    public BitstampMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Bitstamp runtime: subscribe payload строится как multi-message последовательность")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableBitstampClient();
        var payloads = client.BuildSubscribePayloads(["btcusd", "ethusd"])
            .Select(payload => JsonDocument.Parse(payload.ToArray()))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(2));
        Assert.That(payloads[0].RootElement.GetProperty("event").GetString(), Is.EqualTo("bts:subscribe"));
        Assert.That(payloads.Select(document => document.RootElement.GetProperty("data").GetProperty("channel").GetString()),
            Is.EqualTo(new[] { "live_trades_btcusd", "live_trades_ethusd" }));

        foreach (var document in payloads)
            document.Dispose();
    }

    [TestCase(TestName = "Bitstamp runtime: unsubscribe payload строится как multi-message последовательность")]
    public void UnsubscribePayloadGeneration()
    {
        var client = new TestableBitstampClient();
        var payloads = client.BuildUnsubscribePayloads(["btcusd"])
            .Select(payload => JsonDocument.Parse(payload.ToArray()))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(1));
        Assert.That(payloads[0].RootElement.GetProperty("event").GetString(), Is.EqualTo("bts:unsubscribe"));
        Assert.That(payloads[0].RootElement.GetProperty("data").GetProperty("channel").GetString(), Is.EqualTo("live_trades_btcusd"));

        foreach (var document in payloads)
            document.Dispose();
    }

    [TestCase(TestName = "Bitstamp runtime: trade payload публикует MarketUpdateReceived")]
    public async Task TradePayloadPublishesMarketUpdate()
    {
        var client = new TestableBitstampClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "trade",
              "channel": "live_trades_btcusd",
              "data": {
                "price": "66500.70",
                "amount": "0.25"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("btcusd"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(66500.70));
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Trade));
    }

    [TestCase(TestName = "Bitstamp runtime: invalid trade price не публикует update")]
    public async Task InvalidTradePriceDoesNotPublishUpdate()
    {
        var client = new TestableBitstampClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "trade",
              "channel": "live_trades_btcusd",
              "data": {
                "price": "not-a-number"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: unknown channel игнорируется без update")]
    public async Task UnknownChannelDoesNotPublishUpdate()
    {
        var client = new TestableBitstampClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "trade",
              "channel": "diff_order_book_btcusd",
              "data": {
                "price": "66500.70"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: unknown event игнорируется без update")]
    public async Task UnknownEventDoesNotPublishUpdate()
    {
        var client = new TestableBitstampClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "order_created",
              "channel": "live_trades_btcusd",
              "data": {
                "price": "66500.70"
              }
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableBitstampClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "bts:subscription_succeeded",
              "channel": "live_trades_btcusd"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["btcusd"]));
    }

    [TestCase(TestName = "Bitstamp runtime: subscribe ack с неизвестным channel не публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckWithUnknownChannelDoesNotPublishSubscriptionAcknowledged()
    {
        var client = new TestableBitstampClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "bts:subscription_succeeded",
              "channel": "diff_order_book_btcusd"
            }
            """);

        Assert.That(received, Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: unsubscribe success игнорируется без ack or error")]
    public async Task UnsubscribeSuccessDoesNotPublishEvents()
    {
        var client = new TestableBitstampClient();
        MarketSubscriptionEventArgs? acknowledged = null;
        MarketRuntimeErrorEventArgs? runtimeError = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            acknowledged = args;
            return ValueTask.CompletedTask;
        };

        client.RuntimeError += (sender, args) =>
        {
            runtimeError = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "bts:unsubscription_succeeded",
              "channel": "live_trades_btcusd"
            }
            """);

        using var scope = Assert.EnterMultipleScope();
        Assert.That(acknowledged, Is.Null);
        Assert.That(runtimeError, Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableBitstampClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "bts:error",
              "message": "invalid channel"
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("invalid channel"));
    }

    [TestCase(TestName = "Bitstamp runtime: error payload с data.message публикует RuntimeError")]
    public async Task ErrorPayloadWithDataMessagePublishesRuntimeError()
    {
        var client = new TestableBitstampClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectPayloadAsync("""
            {
              "event": "bts:error",
              "data": {
                "message": "subscription failed"
              }
            }
            """);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("subscription failed"));
    }

    [TestCase(TestName = "Bitstamp runtime: price stream получает обновление через runtime bridge")]
    public async Task PriceStreamReceivesRuntimeBridgeUpdate()
    {
        var client = new TestableBitstampClient();
        using var stream = new BitstampPriceStream(client);

        await client.InjectPayloadAsync("""
            {
              "event": "trade",
              "channel": "live_trades_ethusd",
              "data": {
                "price": "3200.50"
              }
            }
            """);

        var snapshot = stream.GetPrice("ethusd");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.LastTradePrice, Is.EqualTo(3200.50));
    }

    [TestCase(TestName = "Bitstamp runtime: dispose bridge останавливает запись в price stream")]
    public async Task DisposedPriceStreamStopsReceivingUpdates()
    {
        var client = new TestableBitstampClient();
        var stream = new BitstampPriceStream(client);

        stream.Dispose();

        await client.InjectPayloadAsync("""
            {
              "event": "trade",
              "channel": "live_trades_solusd",
              "data": {
                "price": "150.50"
              }
            }
            """);

        Assert.That(stream.GetPrice("solusd"), Is.Null);
    }

    [TestCase(TestName = "Bitstamp runtime: reconnect восстанавливает подписки через multi-message base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        using var client = new TestableRuntimeBitstampClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["btcusd", "ethusd"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages, Is.EqualTo([
            "{\"event\":\"bts:subscribe\",\"data\":{\"channel\":\"live_trades_btcusd\"}}",
            "{\"event\":\"bts:subscribe\",\"data\":{\"channel\":\"live_trades_ethusd\"}}",
            "{\"event\":\"bts:subscribe\",\"data\":{\"channel\":\"live_trades_btcusd\"}}",
            "{\"event\":\"bts:subscribe\",\"data\":{\"channel\":\"live_trades_ethusd\"}}"
        ]));
        Assert.That(args.MarketIds, Is.EquivalentTo(["btcusd", "ethusd"]));
    }

    private sealed class TestableBitstampClient : BitstampClient
    {
        public IEnumerable<ReadOnlyMemory<byte>> BuildSubscribePayloads(string[] marketIds) =>
            BuildSubscribeMessages(marketIds);

        public IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribePayloads(string[] marketIds) =>
            BuildUnsubscribeMessages(marketIds);

        public ValueTask InjectPayloadAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    private sealed class TestableRuntimeBitstampClient(TimeSpan reconnectDelay = default)
        : BitstampClient(reconnectDelay: reconnectDelay)
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