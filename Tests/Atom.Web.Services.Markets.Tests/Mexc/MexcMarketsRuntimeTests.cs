using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Atom.Web.Services.Markets;
using Atom.Web.Services.Mexc;

namespace Atom.Web.Services.Mexc.Tests;

/// <summary>
/// Runtime-тесты MexcClient после переноса на ExchangeClientBase.
/// </summary>
public class MexcMarketsRuntimeTests(ILogger logger) : BenchmarkTests<MexcMarketsRuntimeTests>(logger)
{
    public MexcMarketsRuntimeTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "MEXC runtime: subscribe payload строится для book ticker и mini ticker")]
    public void SubscribePayloadGeneration()
    {
        var client = new TestableMexcClient();
        var payloads = client.BuildSubscribePayloads(["btcusdt"])
            .Select(payload => Encoding.UTF8.GetString(payload.Span))
            .ToArray();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(payloads, Has.Length.EqualTo(2));
        Assert.That(payloads[0], Is.EqualTo("{\"method\":\"SUBSCRIPTION\",\"params\":[\"spot@public.aggre.bookTicker.v3.api.pb@100ms@BTCUSDT\"]}"));
        Assert.That(payloads[1], Is.EqualTo("{\"method\":\"SUBSCRIPTION\",\"params\":[\"spot@public.miniTicker.v3.api.pb@BTCUSDT@UTC+0\"]}"));
    }

    [TestCase(TestName = "MEXC runtime: ping payload использует JSON PING")]
    public void PingPayloadGeneration()
    {
        var client = new TestableMexcClient();
        Assert.That(Encoding.UTF8.GetString(client.BuildPingPayload().Span), Is.EqualTo("{\"method\":\"PING\"}"));
    }

    [TestCase(TestName = "MEXC runtime: subscribe ack публикует SubscriptionAcknowledged")]
    public async Task SubscribeAckPublishesSubscriptionAcknowledged()
    {
        var client = new TestableMexcClient();
        MarketSubscriptionEventArgs? received = null;

        client.SubscriptionAcknowledged += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectJsonAsync("{\"id\":0,\"code\":0,\"msg\":\"spot@public.aggre.bookTicker.v3.api.pb@100ms@BTCUSDT\"}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.MarketIds, Is.EquivalentTo(["btcusdt"]));
    }

    [TestCase(TestName = "MEXC runtime: protobuf aggre book ticker публикует bid ask update")]
    public async Task ProtobufBookTickerPublishesMarketUpdate()
    {
        var client = new TestableMexcClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectBinaryAsync(ProtobufEncoder.CreateAggreBookTickerUpdate("BTCUSDT", 52732.88, 52732.89, 1736412092433));

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("btcusdt"));
        Assert.That(received.Value.BestBid, Is.EqualTo(52732.88));
        Assert.That(received.Value.BestAsk, Is.EqualTo(52732.89));
        Assert.That(received.Value.LastTradePrice, Is.Null);
        Assert.That(received.Value.Kind, Is.EqualTo(MarketRealtimeUpdateKind.Ticker));
    }

    [TestCase(TestName = "MEXC runtime: protobuf mini ticker публикует last trade update")]
    public async Task ProtobufMiniTickerPublishesLastTradeUpdate()
    {
        var client = new TestableMexcClient();
        MarketRealtimeUpdate? received = null;

        client.MarketUpdateReceived += (sender, args) =>
        {
            received = args.Update;
            return ValueTask.CompletedTask;
        };

        await client.InjectBinaryAsync(ProtobufEncoder.CreateMiniTickerUpdate("BTCUSDT", 52735.63, 1736412092500));

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Value.AssetId, Is.EqualTo("btcusdt"));
        Assert.That(received.Value.LastTradePrice, Is.EqualTo(52735.63));
        Assert.That(received.Value.BestBid, Is.Null);
        Assert.That(received.Value.BestAsk, Is.Null);
    }

    [TestCase(TestName = "MEXC runtime: price stream merge сохраняет bid ask и last trade из разных pb-сообщений")]
    public async Task PriceStreamMergePreservesPartialUpdates()
    {
        var client = new TestableMexcClient();
        using var stream = new MexcPriceStream(client);

        await client.InjectBinaryAsync(ProtobufEncoder.CreateAggreBookTickerUpdate("BTCUSDT", 52732.88, 52732.89, 1736412092433));
        await client.InjectBinaryAsync(ProtobufEncoder.CreateMiniTickerUpdate("BTCUSDT", 52735.63, 1736412092500));

        var snapshot = stream.GetPrice("btcusdt");

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.BestBid, Is.EqualTo(52732.88));
        Assert.That(snapshot.BestAsk, Is.EqualTo(52732.89));
        Assert.That(snapshot.LastTradePrice, Is.EqualTo(52735.63));
    }

    [TestCase(TestName = "MEXC runtime: error payload публикует RuntimeError")]
    public async Task ErrorPayloadPublishesRuntimeError()
    {
        var client = new TestableMexcClient();
        MarketRuntimeErrorEventArgs? received = null;

        client.RuntimeError += (sender, args) =>
        {
            received = args;
            return ValueTask.CompletedTask;
        };

        await client.InjectJsonAsync("{\"id\":7,\"code\":30001,\"msg\":\"invalid topic\"}");

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.Exception.Message, Does.Contain("MEXC WebSocket error 30001: invalid topic"));
    }

    [TestCase(TestName = "MEXC runtime: reconnect восстанавливает обе подписки через base runtime")]
    public async Task ReconnectResubscribesThroughBaseRuntime()
    {
        await using var client = new TestableRuntimeMexcClient(reconnectDelay: TimeSpan.FromMilliseconds(10));
        var reconnected = new TaskCompletionSource<MarketReconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Reconnected += (sender, args) =>
        {
            reconnected.TrySetResult(args);
            return ValueTask.CompletedTask;
        };

        client.EnqueueCloseMessage();
        await client.SubscribeAsync(["btcusdt"]);

        var args = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var scope = Assert.EnterMultipleScope();
        Assert.That(client.ConnectCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(client.SentMessages.Count(message => message.Contains("spot@public.aggre.bookTicker.v3.api.pb@100ms@BTCUSDT", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(client.SentMessages.Count(message => message.Contains("spot@public.miniTicker.v3.api.pb@BTCUSDT@UTC+0", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(args.MarketIds, Is.EquivalentTo(["btcusdt"]));
    }

    private sealed class TestableMexcClient : MexcClient
    {
        public IEnumerable<ReadOnlyMemory<byte>> BuildSubscribePayloads(string[] marketIds) =>
            BuildSubscribeMessages(marketIds);

        public ReadOnlyMemory<byte> BuildPingPayload() => BuildPingMessage();

        public ValueTask InjectJsonAsync(string json) =>
            OnMessageReceivedAsync(Encoding.UTF8.GetBytes(json), CancellationToken.None);

        public ValueTask InjectBinaryAsync(byte[] payload) =>
            OnMessageReceivedAsync(payload, CancellationToken.None);
    }

    private sealed class TestableRuntimeMexcClient(TimeSpan reconnectDelay = default)
        : MexcClient(reconnectDelay: reconnectDelay)
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
            WebSocketMessageType messageType,
            bool endOfMessage,
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

    private static class ProtobufEncoder
    {
        public static byte[] CreateAggreBookTickerUpdate(string symbol, double bestBid, double bestAsk, long sendTime)
        {
            var body = EncodeMessage(
                EncodeStringField(1, bestBid.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                EncodeStringField(3, bestAsk.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            return EncodeMessage(
                EncodeStringField(1, $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{symbol}"),
                EncodeStringField(3, symbol),
                EncodeVarintField(6, (ulong)sendTime),
                EncodeBytesField(315, body));
        }

        public static byte[] CreateMiniTickerUpdate(string symbol, double lastTradePrice, long sendTime)
        {
            var body = EncodeMessage(
                EncodeStringField(2, lastTradePrice.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            return EncodeMessage(
                EncodeStringField(1, $"spot@public.miniTicker.v3.api.pb@{symbol}@UTC+0"),
                EncodeStringField(3, symbol),
                EncodeVarintField(6, (ulong)sendTime),
                EncodeBytesField(309, body));
        }

        private static byte[] EncodeMessage(params byte[][] parts)
        {
            var length = parts.Sum(part => part.Length);
            var buffer = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
                offset += part.Length;
            }

            return buffer;
        }

        private static byte[] EncodeStringField(int fieldNumber, string value) =>
            EncodeBytesField(fieldNumber, Encoding.UTF8.GetBytes(value));

        private static byte[] EncodeBytesField(int fieldNumber, byte[] value)
        {
            var key = EncodeVarint((ulong)((fieldNumber << 3) | 2));
            var length = EncodeVarint((ulong)value.Length);
            return EncodeMessage(key, length, value);
        }

        private static byte[] EncodeVarintField(int fieldNumber, ulong value)
        {
            var key = EncodeVarint((ulong)(fieldNumber << 3));
            return EncodeMessage(key, EncodeVarint(value));
        }

        private static byte[] EncodeVarint(ulong value)
        {
            var bytes = new List<byte>(10);
            do
            {
                var current = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    current |= 0x80;

                bytes.Add(current);
            }
            while (value != 0);

            return [.. bytes];
        }
    }
}