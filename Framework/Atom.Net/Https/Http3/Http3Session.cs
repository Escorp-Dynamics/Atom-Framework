using System.Buffers;
using System.Globalization;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Headers.QPack;
using Atom.Net.Quic;

namespace Atom.Net.Https.Http3;

/// <summary>
/// Сеанс HTTP/3 поверх установленного соединения QUIC.
/// </summary>
/// <remarks>
/// Разделение обязанностей здесь глубже, чем в HTTP/2. Мультиплексирование, управление потоком,
/// подтверждения и восстановление порядка забрал себе QUIC; сеансу остаются разметка кадрами,
/// сжатие заголовков QPACK и служебные однонаправленные потоки.
///
/// Служебные потоки открываются СРАЗУ и до первого запроса: управляющий поток с параметрами и два
/// потока QPACK. Их отсутствие сервер расценивает как нарушение и закрывает соединение — даже
/// если динамическую таблицу мы не используем вовсе.
/// </remarks>
/// <param name="connection">Установленное соединение QUIC.</param>
/// <param name="settings">Параметры HTTP/3, наблюдаемые сервером.</param>
/// <param name="pseudoHeaderOrder">Порядок псевдозаголовков, заданный профилем браузера.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Соединением QUIC сеанс не владеет: его создаёт и закрывает вызывающая сторона, иначе оно закрылось бы дважды.")]
public sealed class Http3Session(QuicConnection connection, IReadOnlyList<(Http3SettingId Id, ulong Value)> settings, string pseudoHeaderOrder = "masp") : IAsyncDisposable
{
    /// <summary>Тип управляющего однонаправленного потока.</summary>
    private const ulong ControlStreamType = 0x00;

    /// <summary>Тип потока кодировщика QPACK.</summary>
    private const ulong QpackEncoderStreamType = 0x02;

    /// <summary>Тип потока декодировщика QPACK.</summary>
    private const ulong QpackDecoderStreamType = 0x03;

    private readonly QuicConnection connection = connection;
    private readonly QPackEncoder encoder = new();
    /// <summary>
    /// Декодировщик заголовков ответа.
    /// </summary>
    /// <remarks>
    /// ★ Ёмкость таблицы берётся ИЗ ОБЪЯВЛЕННЫХ НАМИ ЖЕ параметров, а не из значения по умолчанию.
    /// Это не удобство, а условие работоспособности: сервер заполняет динамическую таблицу ровно
    /// настолько, насколько мы разрешили, и ссылается на её записи. Декодировщик с меньшей
    /// таблицей вытесняет их раньше и падает с «запись уже вытеснена» — что и происходило с
    /// Google, объявившим себе 65536 при нашей таблице в 4096. Cloudflare таблицей пользуется
    /// скупо, поэтому там всё работало, и расхождение не проявлялось.
    /// </remarks>
    private readonly QPackDecoder decoder = new(FindQpackCapacity(settings));

    /// <summary>
    /// Открывает служебные потоки и отправляет параметры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача, завершающаяся после отправки параметров.</returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        // Разбор служебных потоков сервера запускаем ДО отправки запросов: инструкции QPACK могут
        // прийти раньше первого ответа, и не применив их, мы не разберём его заголовки.
        serviceLoop = Task.Run(() => ReadServiceStreamsAsync(lifetime.Token), CancellationToken.None);

        var control = connection.OpenUnidirectionalStream();

        var buffer = new byte[256];
        var offset = WriteStreamType(buffer, ControlStreamType);
        offset += Http3Frame.WriteSettings(buffer.AsSpan(offset), settings);

        // Вслед за параметрами — подставной кадр зарезервированного типа. Так делает браузер, а
        // получатель обязан его пропустить (RFC 9114, §7.2.8).
        offset += Http3Frame.WriteGrease(buffer.AsSpan(offset));

        await connection.SendAsync(control, buffer.AsMemory(0, offset), fin: false, cancellationToken).ConfigureAwait(false);

        // Потоки QPACK обязаны существовать, даже когда динамическая таблица не используется:
        // сервер вправе начать слать по ним свои указания в любой момент.
        await OpenServiceStreamAsync(QpackEncoderStreamType, cancellationToken).ConfigureAwait(false);
        await OpenServiceStreamAsync(QpackDecoderStreamType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет запрос и возвращает поток, по которому придёт ответ.
    /// </summary>
    /// <param name="method">Метод.</param>
    /// <param name="authority">Узел с портом, если он не стандартный.</param>
    /// <param name="path">Путь с параметрами запроса.</param>
    /// <param name="headers">Прикладные заголовки.</param>
    /// <param name="body">Тело запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Поток ответа.</returns>
    public async ValueTask<QuicStream> SendRequestAsync(
        string method,
        string authority,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(headers);

        // ★ Порядок псевдозаголовков задаёт ПРОФИЛЬ, а не код сеанса. Прежде он был зашит
        // хромиумовским для всех: клиент с профилем Firefox отдавал по TCP свой порядок mpas, а
        // по QUIC — masp, то есть противоречил сам себе на одном и том же узле, стоило серверу
        // объявить alt-svc. Порядок наблюдаем и входит в отпечаток HTTP/3 ровно так же, как в
        // HTTP/2, где профильным он давно стал.
        var fields = new List<KeyValuePair<string, string>>(headers.Count + 4);

        foreach (var pseudo in pseudoHeaderOrder)
        {
            fields.Add(pseudo switch
            {
                'm' => new KeyValuePair<string, string>(":method", method),
                'a' => new KeyValuePair<string, string>(":authority", authority),
                's' => new KeyValuePair<string, string>(":scheme", "https"),
                'p' => new KeyValuePair<string, string>(":path", path),
                _ => throw new InvalidOperationException($"Неизвестный псевдозаголовок '{pseudo}' в порядке профиля"),
            });
        }

        if (fields.Count is not 4) throw new InvalidOperationException("Порядок псевдозаголовков обязан называть все четыре");

        fields.AddRange(headers);

        if (!body.IsEmpty && !fields.Exists(static field => string.Equals(field.Key, "content-length", StringComparison.OrdinalIgnoreCase)))
            fields.Add(new KeyValuePair<string, string>("content-length", body.Length.ToString(CultureInfo.InvariantCulture)));

        var block = new ArrayBufferWriter<byte>(512);
        encoder.Encode(block, fields);

        var stream = connection.OpenBidirectionalStream();

        var headerLength = Http3Frame.GetHeaderLength(Http3FrameType.Headers, block.WrittenCount);
        var dataHeaderLength = body.IsEmpty ? 0 : Http3Frame.GetHeaderLength(Http3FrameType.Data, body.Length);
        var total = headerLength + block.WrittenCount + dataHeaderLength + body.Length;

        // Буфер запроса берётся из пула: на каждый запрос он свой, живёт до конца отправки и
        // возвращается сразу — держать его дольше незачем, данные уже ушли в сокет.
        var payload = ArrayPool<byte>.Shared.Rent(total);

        try
        {
            var offset = Http3Frame.WriteHeader(payload, Http3FrameType.Headers, block.WrittenCount);

            block.WrittenSpan.CopyTo(payload.AsSpan(offset));
            offset += block.WrittenCount;

            if (!body.IsEmpty)
            {
                offset += Http3Frame.WriteHeader(payload.AsSpan(offset), Http3FrameType.Data, body.Length);
                body.Span.CopyTo(payload.AsSpan(offset));
                offset += body.Length;
            }

            await connection.SendAsync(stream, payload.AsMemory(0, offset), fin: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }

        return stream;
    }

    /// <summary>
    /// Находит объявленную нами ёмкость динамической таблицы QPACK.
    /// </summary>
    /// <param name="settings">Параметры HTTP/3, которые уходят серверу.</param>
    /// <returns>Ёмкость в байтах.</returns>
    private static int FindQpackCapacity(IReadOnlyList<(Http3SettingId Id, ulong Value)> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        for (var index = 0; index < settings.Count; index++)
        {
            if (settings[index].Id is Http3SettingId.QpackMaxTableCapacity) return (int)Math.Min(settings[index].Value, int.MaxValue);
        }

        // Ноль означает отказ от динамической таблицы — тогда и декодировщику она не нужна.
        return 0;
    }

    /// <summary>
    /// Снимает завершённый поток запроса с учёта соединения.
    /// </summary>
    /// <param name="stream">Поток запроса.</param>
    /// <remarks>
    /// Обязательно после КАЖДОГО запроса, в том числе завершившегося ошибкой: иначе таблица
    /// потоков соединения растёт без границы, а соединение из пула живёт долго.
    /// </remarks>
    public void ReleaseStream(QuicStream stream) => connection.ReleaseStream(stream);

    /// <summary>
    /// Читает ответ из потока: заголовки и тело.
    /// </summary>
    /// <param name="stream">Поток запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Заголовки и тело ответа.</returns>
    /// <remarks>
    /// Кадры HTTP/3 свободно пересекают границы кусков потока, поэтому данные накапливаются, пока
    /// не соберётся целый кадр. Разбирать половину кадра нельзя: длина известна только из его
    /// заголовка.
    /// </remarks>
    public async ValueTask<(IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body)> ReadResponseAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var frames = new StreamFrameBuffer(1024);
        var headers = new List<KeyValuePair<string, string>>();

        // ★ Отложенный блок принадлежит ОТВЕТУ, а не сеансу. Полем сеанса он был общим на все
        // запросы соединения: при двух параллельных запросах к серверу, пользующемуся
        // динамической таблицей QPACK, ответ A откладывал свой блок, а ответ B на следующем
        // витке разбирал его и приписывал себе чужие заголовки — вплоть до чужого :status и
        // чужих кук. Сам A при этом оставался без заголовков и падал по таймауту ожидания
        // вставок. Мультиплексирование здесь до сотни потоков на соединение, так что случай
        // рядовой, а не редкий.
        byte[]? blockedHeaderBlock = null;

        // Тело выделяется по факту первого кадра данных: ответы без тела встречаются чаще всего,
        // и заранее выделенный буфер под них — чистая потеря.
        ArrayBufferWriter<byte>? body = null;

        // Ожидание и разбор разделены намеренно: дождавшись данных один раз, забираем ВСЁ
        // накопленное, а не платим за ожидание на каждом куске.
        while (await stream.Body.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (stream.Body.TryRead(out var chunk))
            {
                frames.Append(chunk.Span);
            }

            while (true)
            {
                var available = frames.Available;

                if (!Http3Frame.TryReadHeader(available, out var type, out var length, out var headerLength)) break;
                if ((ulong)(available.Length - headerLength) < length) break;

                var payload = available.Slice(headerLength, (int)length);

                if (type is Http3FrameType.Headers)
                {
                    // Блок может ссылаться на вставки, которые ещё идут по встречному потоку
                    // кодировщика — мы сами объявили серверу право так делать (SETTINGS
                    // QPACK_BLOCKED_STREAMS). Дождаться их — штатная часть разбора, а не ошибка:
                    // без ожидания такой ответ просто не читается.
                    if (!TryDecodeHeaders(payload, headers)) blockedHeaderBlock = payload.ToArray();
                }
                else if (type is Http3FrameType.Data && !payload.IsEmpty)
                {
                    body ??= new ArrayBufferWriter<byte>(Math.Max(payload.Length, 1024));
                    body.Write(payload);
                }

                frames.Consume(headerLength + (int)length);
            }

            // Отложенный блок пробуем снова: за время ожидания могли прийти нужные вставки.
            // Сам кадр из накопителя уже снят — откладывается только его разбор.
            if (blockedHeaderBlock is { } pending && TryDecodeHeaders(pending, headers)) blockedHeaderBlock = null;
        }

        // Ответ дочитан, но его заголовки могли остаться заблокированными: вставки идут по
        // ДРУГОМУ потоку соединения и вполне могут отстать от самого ответа. Ждём их — ровно то,
        // на что мы дали серверу право параметром QPACK_BLOCKED_STREAMS.
        if (blockedHeaderBlock is not null) blockedHeaderBlock = await AwaitBlockedHeadersAsync(blockedHeaderBlock, headers, cancellationToken).ConfigureAwait(false);

        return (headers, body is null ? [] : body.WrittenSpan.ToArray());
    }

    /// <summary>
    /// Предел ожидания недостающих вставок QPACK.
    /// </summary>
    /// <remarks>
    /// Ждать бесконечно нельзя: сервер, не приславший вставок вовсе, подвесил бы запрос молча.
    /// Секунды с запасом хватает — вставки идут по тому же соединению и приходят практически
    /// одновременно с ответом.
    /// </remarks>
    private static readonly TimeSpan BlockedHeadersTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Дожидается вставок, которых не хватило для разбора заголовков.
    /// </summary>
    /// <param name="pending">Блок заголовков, который пока не разбирается.</param>
    /// <param name="headers">Куда складывать разобранные заголовки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="null"/>, когда блок наконец разобран.</returns>
    private async ValueTask<byte[]?> AwaitBlockedHeadersAsync(byte[] pending, List<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(BlockedHeadersTimeout);

        while (true)
        {
            var arrival = decoder.WaitForInsertionsAsync();

            if (TryDecodeHeaders(pending, headers)) return null;

            try
            {
                await arrival.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Вставки QPACK, на которые ссылается ответ, не пришли за {BlockedHeadersTimeout.TotalSeconds:F0} с (принято {decoder.InsertCount})");
            }
        }
    }

    /// <summary>
    /// Пробует разобрать блок заголовков.
    /// </summary>
    /// <param name="block">Блок QPACK.</param>
    /// <param name="destination">Куда складывать разобранные заголовки.</param>
    /// <returns><see langword="false"/>, если блок заблокирован недостающими вставками.</returns>
    private bool TryDecodeHeaders(ReadOnlySpan<byte> block, List<KeyValuePair<string, string>> destination)
    {
        try
        {
            destination.AddRange(decoder.Decode(block));
            return true;
        }
        catch (QPackBlockedException)
        {
            return false;
        }
    }

    private readonly CancellationTokenSource lifetime = new();
    private Task? serviceLoop;

    /// <summary>Параметры, присланные сервером.</summary>
    public IReadOnlyDictionary<ulong, ulong> PeerSettings => peerSettings;

    private readonly Dictionary<ulong, ulong> peerSettings = [];

    /// <summary>Сервер прислал GOAWAY: новые запросы по этому соединению недопустимы.</summary>
    public bool IsGoingAway { get; private set; }

    /// <summary>
    /// Читает служебные однонаправленные потоки сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача цикла.</returns>
    /// <remarks>
    /// Первое число в однонаправленном потоке — его ТИП, и до него поток не значит ничего.
    /// Управляющий несёт параметры и GOAWAY, поток кодировщика QPACK — инструкции для
    /// динамической таблицы. Без последних сервер вправе ссылаться на записи, которых у нас нет,
    /// и разбор заголовков падает.
    /// </remarks>
    private async Task ReadServiceStreamsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var stream in connection.IncomingStreams.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Двунаправленные потоки сервер клиенту не открывает; всё, что пришло, — служебное.
                _ = Task.Run(() => ConsumeServiceStreamAsync(stream, cancellationToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение.
        }
    }

    private async Task ConsumeServiceStreamAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        using var frames = new StreamFrameBuffer(256);
        var type = ulong.MaxValue;

        try
        {
            while (await stream.Body.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (stream.Body.TryRead(out var chunk))
                {
                    frames.Append(chunk.Span);
                }

                if (type is ulong.MaxValue)
                {
                    if (!QuicCodec.TryRead(frames.Available, out type, out var read)) continue;

                    frames.Consume(read);
                }

                if (type is QpackEncoderStreamType)
                {
                    // Инструкции применяются по мере поступления: держать их до конца потока
                    // нельзя, поток кодировщика не заканчивается никогда.
                    //
                    // Снимаем РОВНО столько, сколько разобрано целиком: кусок QUIC вполне может
                    // разрезать инструкцию, и выбросив её остаток, мы навсегда разошлись бы с
                    // серверной таблицей.
                    if (frames.Length is 0) continue;

                    frames.Consume(decoder.ApplyEncoderInstructions(frames.Available));
                    continue;
                }

                if (type is ControlStreamType) ConsumeControlStream(frames);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение.
        }
        catch (InvalidOperationException)
        {
            // Служебный поток повреждён: запросы это не отменяет.
        }
    }

    private void ConsumeControlStream(StreamFrameBuffer frames)
    {
        while (true)
        {
            var available = frames.Available;

            if (!Http3Frame.TryReadHeader(available, out var frameType, out var length, out var headerLength)) return;
            if ((ulong)(available.Length - headerLength) < length) return;

            var payload = available.Slice(headerLength, (int)length);

            if (frameType is Http3FrameType.Settings) ReadSettings(payload);
            else if (frameType is Http3FrameType.GoAway) IsGoingAway = true;

            frames.Consume(headerLength + (int)length);
        }
    }

    private void ReadSettings(ReadOnlySpan<byte> payload)
    {
        var offset = 0;

        while (offset < payload.Length)
        {
            if (!QuicCodec.TryRead(payload[offset..], out var id, out var read)) return;
            offset += read;

            if (!QuicCodec.TryRead(payload[offset..], out var value, out read)) return;
            offset += read;

            peerSettings[id] = value;
        }
    }

    private ValueTask OpenServiceStreamAsync(ulong type, CancellationToken cancellationToken)
    {
        var stream = connection.OpenUnidirectionalStream();

        var buffer = new byte[8];
        var offset = WriteStreamType(buffer, type);

        return connection.SendAsync(stream, buffer.AsMemory(0, offset), fin: false, cancellationToken);
    }

    /// <summary>
    /// Останавливает разбор служебных потоков.
    /// </summary>
    /// <returns>Задача завершения.</returns>
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);

        if (serviceLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await serviceLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при закрытии.
            }
        }

        lifetime.Dispose();
    }

    /// <summary>
    /// Пишет тип однонаправленного потока — его первое поле.
    /// </summary>
    private static int WriteStreamType(Span<byte> destination, ulong type)
    {
        if (!QuicCodec.TryWrite(destination, type, out var written)) throw new InvalidOperationException("Не хватило места для типа потока HTTP/3");
        return written;
    }
}
