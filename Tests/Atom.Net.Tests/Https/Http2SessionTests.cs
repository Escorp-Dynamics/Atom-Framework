using System.Buffers.Binary;
using System.Linq;
using System.Threading.Channels;
using Atom.Net.Https.Http;
using Atom.Net.Https.Http2;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет сеанс HTTP/2 на паре потоков в памяти — без сети.
/// </summary>
/// <remarks>
/// Мультиплексирование и управление потоком проверяются здесь, а не на живом сервере, по двум
/// причинам. Первая: живой сервер не даст воспроизвести редкие последовательности кадров — GOAWAY
/// посреди обмена, блок заголовков в нескольких CONTINUATION, изменение SETTINGS на ходу. Вторая:
/// такие проверки обязаны быть детерминированными, а сетевой обмен таковым не бывает.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Http2SessionTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public async Task ConcurrentRequestsGetIncreasingStreamIdsInWireOrder()
    {
        // Порядок номеров обязан совпадать с порядком кадров на проводе: сервер закрывает
        // соединение с PROTOCOL_ERROR, увидев HEADERS с номером меньше уже открытого.
        await using var peer = new LoopbackPeer();
        await using var session = new Http2Session(peer.ClientSide, Http2ProfileCatalog.CreateChrome());
        await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);

        await peer.SkipClientPrefaceAsync();

        // Запросы стартуют одновременно с разных потоков пула: гонка между выдачей номера и
        // записью кадра проявляется только под реальной состязательностью, а на последовательном
        // запуске выглядит исправной.
        const int RequestCount = 64;
        using var startGate = new SemaphoreSlim(0, RequestCount);

        var requests = Enumerable.Range(0, RequestCount)
            .Select(_ => Task.Run(async () =>
            {
                await startGate.WaitAsync(TestContext.CurrentContext.CancellationToken);
                var stream = await session.SendRequestAsync(SimpleHeaders(), ReadOnlyMemory<byte>.Empty, TestContext.CurrentContext.CancellationToken);
                return stream.Id;
            }, TestContext.CurrentContext.CancellationToken))
            .ToArray();

        startGate.Release(RequestCount);

        var opened = await Task.WhenAll(requests);
        var wireOrder = await peer.ReadHeaderStreamIdsAsync(opened.Length);

        Assert.Multiple(() =>
        {
            Assert.That(opened.Distinct().Count(), Is.EqualTo(opened.Length), "номера потоков повторились");
            Assert.That(opened.All(static id => id % 2 is 1), Is.True, "клиентские потоки обязаны быть нечётными");
            Assert.That(wireOrder, Is.Ordered.Ascending, "кадры ушли в порядке, нарушающем возрастание номеров");
        });
    }

    [Test]
    public async Task ResponseHeadersSpanningContinuationFramesAreDecodedAsOneBlock()
    {
        // Половина блока HPACK не является корректным блоком: декодировать её отдельно — значит
        // разрушить динамическую таблицу для ВСЕХ последующих ответов соединения.
        await using var peer = new LoopbackPeer();
        await using var session = new Http2Session(peer.ClientSide, Http2ProfileCatalog.CreateChrome());
        await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);
        await peer.SkipClientPrefaceAsync();

        var stream = await session.SendRequestAsync(SimpleHeaders(), ReadOnlyMemory<byte>.Empty, TestContext.CurrentContext.CancellationToken);
        _ = await peer.ReadHeaderStreamIdsAsync(1);

        var encoded = EncodeResponseHeaders();
        var split = encoded.Length / 2;

        await peer.WriteFrameAsync(new Http2FrameHeader(split, Http2FrameType.Headers, flags: 0x00, stream.Id), encoded.AsMemory(0, split));
        await peer.WriteFrameAsync(new Http2FrameHeader(encoded.Length - split, Http2FrameType.Continuation, flags: 0x04, stream.Id), encoded.AsMemory(split));

        var headers = await stream.Headers.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CurrentContext.CancellationToken);

        Assert.That(headers.Any(static header => string.Equals(header.Key, ":status", StringComparison.Ordinal) && string.Equals(header.Value, "200", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task InformationalResponseDoesNotCompleteHeaders()
    {
        // Ответ 1xx предваряет окончательный: опубликовав его, вызывающая сторона получила бы
        // промежуточный статус вместо настоящего.
        await using var peer = new LoopbackPeer();
        await using var session = new Http2Session(peer.ClientSide, Http2ProfileCatalog.CreateChrome());
        await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);
        await peer.SkipClientPrefaceAsync();

        var stream = await session.SendRequestAsync(SimpleHeaders(), ReadOnlyMemory<byte>.Empty, TestContext.CurrentContext.CancellationToken);
        _ = await peer.ReadHeaderStreamIdsAsync(1);

        var informational = EncodeHeaders([new KeyValuePair<string, string>(":status", "103")]);
        await peer.WriteFrameAsync(new Http2FrameHeader(informational.Length, Http2FrameType.Headers, flags: 0x04, stream.Id), informational);

        Assert.That(stream.Headers.IsCompleted, Is.False, "промежуточный ответ завершил ожидание заголовков");

        var final = EncodeResponseHeaders();
        await peer.WriteFrameAsync(new Http2FrameHeader(final.Length, Http2FrameType.Headers, flags: 0x04, stream.Id), final);

        var headers = await stream.Headers.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CurrentContext.CancellationToken);

        Assert.That(headers.Any(static header => string.Equals(header.Value, "200", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task GoAwayKeepsStreamsWithinTheAnnouncedLimit()
    {
        // GOAWAY означает «больше не открывай», а не «всё пропало»: потоки не выше объявленного
        // номера сервер обязуется довести до конца.
        await using var peer = new LoopbackPeer();
        await using var session = new Http2Session(peer.ClientSide, Http2ProfileCatalog.CreateChrome());
        await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);
        await peer.SkipClientPrefaceAsync();

        var first = await session.SendRequestAsync(SimpleHeaders(), ReadOnlyMemory<byte>.Empty, TestContext.CurrentContext.CancellationToken);
        var second = await session.SendRequestAsync(SimpleHeaders(), ReadOnlyMemory<byte>.Empty, TestContext.CurrentContext.CancellationToken);
        _ = await peer.ReadHeaderStreamIdsAsync(2);

        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0, 4), (uint)first.Id);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4, 4), 0);
        await peer.WriteFrameAsync(new Http2FrameHeader(payload.Length, Http2FrameType.GoAway, flags: 0, streamId: 0), payload);

        await WaitUntilAsync(() => second.Headers.IsCompleted);

        var response = EncodeResponseHeaders();
        await peer.WriteFrameAsync(new Http2FrameHeader(response.Length, Http2FrameType.Headers, flags: 0x04, first.Id), response);

        var headers = await first.Headers.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(headers, Is.Not.Empty, "поток в пределах объявленного номера потерял ответ");
            Assert.That(second.Headers.IsFaulted, Is.True, "поток сверх объявленного номера остался ждать");
            Assert.That(session.IsGoingAway, Is.True);
        });
    }

    [Test]
    public async Task ServerSettingsReplaceAssumedConcurrencyLimit()
    {
        // Ограничивает нас предел СЕРВЕРА: открыв больше, мы получим REFUSED_STREAM вместо ответов.
        await using var peer = new LoopbackPeer();
        await using var session = new Http2Session(peer.ClientSide, Http2ProfileCatalog.CreateChrome());
        await session.StartAsync(Http2ProfileCatalog.ChromeConnectionWindowIncrement, TestContext.CurrentContext.CancellationToken);
        await peer.SkipClientPrefaceAsync();

        var payload = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), (ushort)Http2SettingId.MaxConcurrentStreams);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(2, 4), 42);
        await peer.WriteFrameAsync(new Http2FrameHeader(payload.Length, Http2FrameType.Settings, flags: 0, streamId: 0), payload);

        await WaitUntilAsync(() => session.PeerMaxConcurrentStreams is 42);

        Assert.That(session.PeerMaxConcurrentStreams, Is.EqualTo(42));
    }

    [Test]
    public void FirefoxStartsRequestsAboveItsPriorityTree()
    {
        // Запрос на потоке, который преамбула уже назвала в дереве приоритетов, сервер не
        // обслуживает — соединение зависает без единой ошибки. Дерево у Firefox 154 пусто, но
        // проверка остаётся именно в таком виде: она страхует не текущее значение, а связь между
        // деревом и первым запросом, если дерево когда-нибудь вернётся.
        var firefox = Http2ProfileCatalog.CreateFirefox();
        var highestReserved = firefox.PriorityTree.Select(static priority => priority.Id).DefaultIfEmpty(0).Max();

        Assert.Multiple(() =>
        {
            Assert.That(firefox.InitialStreamId, Is.GreaterThan(highestReserved));
            Assert.That(firefox.InitialStreamId % 2, Is.EqualTo(1));
        });
    }

    [Test]
    public void ChromeStartsRequestsFromTheFirstStream()
    {
        var chrome = Http2ProfileCatalog.CreateChrome();

        Assert.Multiple(() =>
        {
            Assert.That(chrome.PriorityTree, Is.Empty);
            Assert.That(chrome.InitialStreamId, Is.EqualTo(1));
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10, TestContext.CurrentContext.CancellationToken);
        }

        Assert.Fail("условие не наступило за отведённое время");
    }

    private static List<KeyValuePair<string, string>> SimpleHeaders()
        => [.. Http2RequestHeaders.Build("GET", "example.org", "/", [], Http2ProfileCatalog.CreateChrome())];

    private static byte[] EncodeResponseHeaders()
        => EncodeHeaders([new KeyValuePair<string, string>(":status", "200"), new KeyValuePair<string, string>("content-type", "text/plain")]);

    private static byte[] EncodeHeaders(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>(256);
        var encoder = new Atom.Net.Https.Headers.HPackEncoder(4096);
        encoder.Encode(writer, headers);

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Двусторонний канал в памяти, изображающий удалённую сторону соединения.
    /// </summary>
    /// <remarks>
    /// Свой, а не готовый поток из библиотеки: нужен транспорт типа <see cref="Atom.IO.Stream"/>,
    /// умеющий и отдавать записанное сеансом, и подсовывать ему произвольные кадры. Каналы дают
    /// это без единого системного вызова и без гонок между сторонами.
    /// </remarks>
    private sealed class LoopbackPeer : IAsyncDisposable
    {
        private readonly Channel<byte[]> toPeer = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> toClient = Channel.CreateUnbounded<byte[]>();
        private readonly List<byte> peerBuffer = [];

        public LoopbackPeer() => ClientSide = new ChannelStream(toClient.Reader, toPeer.Writer);

        public Atom.IO.Stream ClientSide { get; }

        public async ValueTask SkipClientPrefaceAsync()
        {
            // Преамбула и SETTINGS уже ушли; вычитываем их, чтобы дальше читать только кадры запросов.
            var expected = Http2Preface.GetRequiredSize(Http2ProfileCatalog.CreateChrome());
            await FillAsync(expected);
            peerBuffer.RemoveRange(0, expected);
        }

        public async ValueTask<int[]> ReadHeaderStreamIdsAsync(int count)
        {
            var ids = new List<int>(count);

            while (ids.Count < count)
            {
                await FillAsync(Http2FrameHeader.Size);
                var header = Http2FrameHeader.Read(CollectionsMarshalSpan(Http2FrameHeader.Size));

                await FillAsync(Http2FrameHeader.Size + header.Length);
                peerBuffer.RemoveRange(0, Http2FrameHeader.Size + header.Length);

                if (header.Type is Http2FrameType.Headers) ids.Add(header.StreamId);
            }

            return [.. ids];
        }

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

        private ReadOnlySpan<byte> CollectionsMarshalSpan(int length) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(peerBuffer)[..length];

        private async ValueTask FillAsync(int length)
        {
            while (peerBuffer.Count < length)
            {
                var chunk = await toPeer.Reader.ReadAsync(TestContext.CurrentContext.CancellationToken);
                peerBuffer.AddRange(chunk);
            }
        }
    }

    /// <summary>
    /// Поток поверх пары каналов.
    /// </summary>
    private sealed class ChannelStream(ChannelReader<byte[]> reader, ChannelWriter<byte[]> writer) : Atom.IO.Stream
    {
        private byte[] pending = [];
        private int pendingOffset;

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (pendingOffset >= pending.Length)
            {
                if (!await reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!reader.TryRead(out var chunk)) continue;

                pending = chunk;
                pendingOffset = 0;
            }

            var take = Math.Min(buffer.Length, pending.Length - pendingOffset);
            pending.AsMemory(pendingOffset, take).CopyTo(buffer);
            pendingOffset += take;

            return take;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Уступка планировщику имитирует задержку сети. Без неё запись успевает завершиться
            // внутри того же кванта, окно между выдачей номера и отправкой кадра не наблюдается,
            // и тест перестаёт различать корректную реализацию и гоночную.
            await Task.Yield();
            await writer.WriteAsync(buffer.ToArray(), cancellationToken);
        }

        public override int Read(Span<byte> buffer) => throw new NotSupportedException();

        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    }
}
