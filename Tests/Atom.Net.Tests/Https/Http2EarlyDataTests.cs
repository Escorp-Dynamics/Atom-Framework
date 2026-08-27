using System.Buffers;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Threading.Channels;
using Atom.Net.Https.Http;
using Atom.Net.Https;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Http2;
using Atom.Net.Https.Headers;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет сборку 0-RTT payload и поведение сеанса h2 при уже отправленной преамбуле.
/// </summary>
[CancelAfter(30000)]
public sealed class Http2EarlyDataTests
{
    [Test]
    public void EarlyPayloadContainsPrefaceSettingsAndTerminalHeaders()
    {
        var (payload, _) = Https2Connection.BuildEarlyRequestPayload(BuildHeaders(), Http2ProfileCatalog.CreateChrome(), Http2ProfileCatalog.ChromeConnectionWindowIncrement);

        // Преамбула обязана открывать payload: это первые байты h2-соединения.
        var magic = System.Text.Encoding.ASCII.GetBytes(Http2Preface.ClientMagic.ToArray().Length is 0 ? "" : "");
        Assert.That(payload.AsSpan(0, 24).ToArray(), Is.EqualTo(System.Text.Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n")));

        // Дальше идут кадры; последний — HEADERS потока 1 с END_STREAM и END_HEADERS.
        var position = 24;
        byte lastType = 0; byte lastFlags = 0; int lastStream = 0;
        while (position + 9 <= payload.Length)
        {
            var length = (payload[position] << 16) | (payload[position + 1] << 8) | payload[position + 2];
            lastType = payload[position + 3];
            lastFlags = payload[position + 4];
            lastStream = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(position + 5, 4)) & 0x7FFFFFFF);
            position += 9 + length;
        }

        Assert.That(lastType, Is.EqualTo((byte)Http2FrameType.Headers), "HEADERS обязаны замыкать 0-RTT payload");
        Assert.That(lastFlags & 0x01, Is.Not.Zero, "запрос без тела обязан нести END_STREAM");
        Assert.That(lastFlags & 0x04, Is.Not.Zero, "блок заголовков обязан быть полным (END_HEADERS)");
        Assert.That(lastStream, Is.EqualTo(1), "первый клиентский поток h2 — номер 1");
        Assert.That(position, Is.EqualTo(payload.Length), "кадры обязаны заполнять payload ровно");
    }

    [Test]
    public async Task SessionWithPreSentRequestSkipsPrefaceAndAnswersOnStreamOne()
    {
        var peer = new RecordingPeer();
        await using (peer)
        {
            var headers = BuildHeaders();
            var (payload, encoder) = Https2Connection.BuildEarlyRequestPayload(headers, Http2ProfileCatalog.CreateChrome(), Http2ProfileCatalog.ChromeConnectionWindowIncrement);
            var settings = Http2ProfileCatalog.CreateChrome();

            await using var session = new Http2Session(peer.ClientSide, settings, connectionPrefaceAlreadySent: true, encoderSeed: encoder);
            await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);

            Assert.That(peer.WrittenBytes, Is.Zero, "при 0-RTT преамбула повторно не отправляется");

            var stream = session.RegisterPreSentRequest();
            Assert.That(stream.Id, Is.EqualTo(1));

            // Сервер отвечает SETTINGS + заголовками ответа на поток 1.
            await peer.WriteFrameAsync(new Http2FrameHeader(0, Http2FrameType.Settings, flags: 0x00, streamId: 0), ReadOnlyMemory<byte>.Empty);
            var block = new ArrayBufferWriter<byte>(64);
            new HPackEncoder(4096).Encode(block, new[]
            {
                new KeyValuePair<string, string>(":status", "200"),
                new KeyValuePair<string, string>("content-length", "2"),
            });
            await peer.WriteFrameAsync(new Http2FrameHeader(block.WrittenCount, Http2FrameType.Headers, flags: 0x05, streamId: 1), block.WrittenMemory);

            var responseHeaders = await stream.Headers.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CurrentContext.CancellationToken);

            Assert.Multiple(() =>
            {
                Assert.That(session.TerminalError, Is.Null, "ошибка цикла чтения: " + (session.TerminalError?.Message ?? "-"));
                Assert.That(peer.ClientReadBytes, Is.GreaterThan(0), "сеанс не читал из транспорта");
                Assert.That(responseHeaders.Count, Is.GreaterThan(0));
            });
        }
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildHeaders()
    {
        var request = new HttpsRequestMessage(HttpMethod.Get, new Uri("https://example.com/path?q=1"));
        return [.. Https2Connection.BuildHeaderList(request, 0, Http2ProfileCatalog.CreateChrome())];
    }

    /// <summary>Пир на каналах: пишет клиенту, читает и считает записанное клиентом.</summary>
    private sealed class RecordingPeer : IAsyncDisposable
    {
        private readonly Channel<byte[]> toPeer = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> toClient = Channel.CreateUnbounded<byte[]>();

        public RecordingPeer() => ClientSide = new ChannelStream(toClient.Reader, toPeer.Writer);

        public ChannelStream ClientSide { get; }

        public int WrittenBytes => toPeer.Reader.Count;

        public int ClientReadBytes => ClientSide.ReadCount;

        public async ValueTask WriteFrameAsync(Http2FrameHeader header, ReadOnlyMemory<byte> payload)
        {
            var frame = new byte[Http2FrameHeader.Size + payload.Length];
            header.Write(frame);
            payload.CopyTo(frame.AsMemory(Http2FrameHeader.Size));
            await toClient.Writer.WriteAsync(frame, TestContext.CurrentContext.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            toClient.Writer.TryComplete();
            toPeer.Writer.TryComplete();
            await ClientSide.DisposeAsync();
        }
    }

    /// <summary>Поток поверх пары каналов.</summary>
    private sealed class ChannelStream(ChannelReader<byte[]> reader, ChannelWriter<byte[]> writer) : Atom.IO.Stream
    {
        public int ReadCount;

        public override bool CanRead => true;
        public override bool CanWrite => true;

        private byte[] pending = [];
        private int pendingOffset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount += 1;
            if (pendingOffset < pending.Length)
            {
                var take = Math.Min(pending.Length - pendingOffset, buffer.Length);
                pending.AsSpan(pendingOffset, take).CopyTo(buffer.Span);
                pendingOffset += take;
                return take;
            }

            var chunk = await reader.ReadAsync(cancellationToken);
            var copy = Math.Min(chunk.Length, buffer.Length);
            chunk.AsSpan(0, copy).CopyTo(buffer.Span);
            if (copy < chunk.Length)
            {
                pending = chunk;
                pendingOffset = copy;
            }

            return copy;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await writer.WriteAsync(buffer.ToArray(), cancellationToken);
        }

        public override void Flush() { }

        public override void Write(ReadOnlySpan<byte> buffer) => writer.WriteAsync(buffer.ToArray()).AsTask().GetAwaiter().GetResult();

        public override int Read(Span<byte> buffer) => ReadAsync(buffer.ToArray()).AsTask().GetAwaiter().GetResult();
    }
}
