using System.Buffers;
using System.Linq;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Http;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Сеанс HTTP/2 поверх установленного транспорта.
/// </summary>
/// <remarks>
/// Разделение обязанностей здесь такое: сеанс владеет мультиплексированием — циклом чтения кадров,
/// таблицей потоков и управлением потоком, — а транспорт и жизненный цикл соединения остаются
/// снаружи. Это позволяет тестировать мультиплексирование на паре потоков в памяти, без сети.
///
/// Многопоточность решена без блокировок на пути данных. Кадры читает ОДИН выделенный цикл и
/// раскладывает их по потокам через <see cref="ConcurrentDictionary{TKey, TValue}"/>; окна
/// изменяются атомарно. Единственная точка сериализации — запись в транспорт: канал байтов один,
/// и кадры обязаны уходить целиком, поэтому запись защищена семафором. Держать его на время
/// ожидания сети нельзя, поэтому под ним выполняется только само копирование в сокет.
/// </remarks>
/// <param name="transport">Установленный транспорт (обычно TLS-поток).</param>
/// <param name="settings">Профиль HTTP/2.</param>
/// <param name="connectionPrefaceAlreadySent">Преамбула и SETTINGS уже ушли в составе 0-RTT.</param>
/// <param name="encoderSeed">Кодировщик, закодировавший ранние заголовки; его таблица продолжает сеанс.</param>
[SuppressMessage("Design", "MA0182:Internal type is apparently never used", Justification = "Используется соединением HTTP/2 и тестами мультиплексирования.")]
public sealed class Http2Session(Stream transport, Http2Settings settings, bool connectionPrefaceAlreadySent = false, HPackEncoder? encoderSeed = null) : IAsyncDisposable
{
    // Параметры connectionPrefaceAlreadySent и encoderSeed описаны в комментариях у полей,
    // которые из них инициализируются (skipConnectionPreface / encoder).
    // Поле объявлено явно: обращение к параметру первичного конструктора из методов делает их
    // «статическими» с точки зрения анализаторов и мешает JIT кешировать ссылку.
    // Транспортом сеанс НЕ владеет: его создаёт и закрывает соединение, поэтому здесь он не
    // освобождается — иначе поток закрылся бы дважды.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Транспорт принадлежит соединению, а не сеансу.")]
    private readonly Stream transport = transport;

    /// <summary>Размер окна по умолчанию из спецификации.</summary>
    private const int DefaultWindowSize = 65535;

    /// <summary>Порог, после которого стоит вернуть серверу окно приёма.</summary>
    private const int WindowUpdateThreshold = 32768;

    /// <summary>Размер кадра по умолчанию из спецификации, пока сервер не объявил свой.</summary>
    private const int DefaultMaxFrameSize = 16384;

    /// <summary>
    /// Предположение о пределе одновременных потоков до получения SETTINGS сервера.
    /// </summary>
    /// <remarks>
    /// Спецификация рекомендует серверам не объявлять значение меньше сотни, и запросы уходят
    /// СРАЗУ после преамбулы — ждать SETTINGS браузеры не станут. Принять здесь «без ограничений»
    /// значило бы на первых миллисекундах выпустить сколько угодно потоков и получить пачку
    /// REFUSED_STREAM; сотня — то, что сервер почти наверняка выдержит.
    /// </remarks>
    private const int DefaultMaxConcurrentStreams = 100;

    private readonly ConcurrentDictionary<int, Http2StreamState> streams = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    /// <summary>
    /// Кодировщик заголовков запроса.
    /// </summary>
    /// <remarks>
    /// ★ Ёмкость — ПРОТОКОЛЬНАЯ по умолчанию (RFC 9113: 4096), а не наша объявленная. Размер
    /// таблицы кодировщика задаёт ПАРТНЁР своими SETTINGS, и до их прихода действует значение по
    /// умолчанию. Взять здесь своё число значило бы индексировать записи сверх того, что сервер
    /// готов хранить, и разойтись с его декодировщиком — ровно та же ошибка, что была в QPACK,
    /// где ёмкость бралась не оттуда.
    ///
    /// Расти таблице разрешает <see cref="HPackEncoder.UpdateDynamicTableSize"/> в начале
    /// следующего блока заголовков — там, где сигнал об изменении и обязан стоять.
    /// </remarks>
    // Seed передаётся при 0-RTT: ранние заголовки кодировались ДО создания сеанса, и его
    // динамическая таблица обязана продолжиться именно с состояния того кодировщика.
    private readonly HPackEncoder encoder = encoderSeed ?? new(DefaultPeerHeaderTableSize);

    /// <summary>Размер таблицы заголовков по умолчанию до прихода SETTINGS партнёра.</summary>
    private const int DefaultPeerHeaderTableSize = 4096;
    private readonly HPackDecoder decoder = new((int)Math.Max(settings.HeaderTableSize, 4096));
    private readonly CancellationTokenSource lifetime = new();

    private int nextStreamId = Math.Max(settings.InitialStreamId | 1, 1);
    private int peerInitialWindowSize = DefaultWindowSize;
    private int connectionSendWindow = DefaultWindowSize;
    private int connectionReceivedSinceUpdate;
    private Task? readerLoop;
    private volatile Exception? terminalError;

    // Параметры ПАРТНЁРА, а не свои. Отправку ограничивают именно они: собственные настройки
    // описывают, что мы готовы принять, и подставлять их в исходящий путь — распространённая
    // ошибка, которая проявляется отказом соединения на первом же крупном запросе.
    private int peerMaxFrameSize = DefaultMaxFrameSize;
    private int peerMaxConcurrentStreams = DefaultMaxConcurrentStreams;
    private int pendingEncoderTableSize = -1;

    // Состояние GOAWAY: потоки с номером не выше объявленного сервер ещё обслужит, и обрывать их
    // нельзя — это превратило бы штатное закрытие соединения в потерю ответов.
    private int goAwayLastStreamId = int.MaxValue;
    private volatile bool isGoingAway;

    // Пробуждение отправителей при возврате окна. Через смену источника завершения, а не опросом:
    // опрос с паузой добавляет задержку каждому крупному запросу и жжёт такты на ровном месте.
    private TaskCompletionSource windowSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Сборка блока заголовков из HEADERS и CONTINUATION. Полей достаточно без синхронизации:
    // с ними работает только цикл чтения, и он один.
    /// <summary>
    /// Предел размера собираемого блока заголовков.
    /// </summary>
    /// <remarks>
    /// С запасом больше всего, что отдают настоящие серверы: их собственные пределы обычно
    /// держатся в десятках килобайт. Нужен не ради экономии, а против бесконечной сборки.
    /// </remarks>
    private const int MaxHeaderBlockLength = 8 * 1024 * 1024;

    /// <summary>Служебные кадры, отложенные из цикла чтения: замок записи был занят.</summary>
    private readonly ConcurrentQueue<PendingControlFrame> controlFrames = new();

    private ArrayBufferWriter<byte>? pendingHeaderBlock;
    private int pendingHeaderStreamId = -1;
    private bool pendingHeaderEndStream;

    /// <summary>Число потоков, по которым сейчас идёт обмен.</summary>
    public int ActiveStreams => streams.Count;

    /// <summary>Причина, по которой сеанс перестал работать, если она известна.</summary>
    public Exception? TerminalError => terminalError;

    /// <summary>
    /// Предел одновременных потоков, объявленный сервером.
    /// </summary>
    /// <remarks>
    /// До получения SETTINGS предел неизвестен и считается неограниченным — так же поступают
    /// браузеры, начиная обмен сразу после преамбулы. Значение важно пулу соединений: превысив
    /// его, мы получим REFUSED_STREAM вместо ответа.
    /// </remarks>
    public int PeerMaxConcurrentStreams => Volatile.Read(ref peerMaxConcurrentStreams);

    /// <summary>Сервер прислал GOAWAY: новые потоки открывать нельзя.</summary>
    public bool IsGoingAway => isGoingAway;

    /// <summary>
    /// Отправляет преамбулу и запускает цикл чтения кадров.
    /// </summary>
    /// <param name="connectionWindowIncrement">Приращение окна соединения из профиля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask StartAsync(uint connectionWindowIncrement, CancellationToken cancellationToken)
    {
        // Преамбула и SETTINGS могут быть уже отправлены в составе 0-RTT (RFC 8446, §2.3):
        // тогда повторная отправка — ошибка кадрирования, а не безобидный дубль.
        if (!connectionPrefaceAlreadySent)
        {
            var size = Http2Preface.GetRequiredSize(settings);
            var buffer = ArrayPool<byte>.Shared.Rent(size);

            try
            {
                var written = Http2Preface.Write(buffer.AsSpan(0, size), settings, connectionWindowIncrement);
                await transport.WriteAsync(buffer.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        readerLoop = Task.Run(() => ReadLoopAsync(lifetime.Token), CancellationToken.None);
    }

    /// <summary>
    /// Регистрирует поток 1, запрос по которому уже ушёл в составе 0-RTT.
    /// </summary>
    /// <returns>Состояние потока для ожидания ответа.</returns>
    /// <remarks>
    /// Запрос отправлен до запуска цикла чтения, поэтому поток обязан появиться в реестре ДО
    /// первого кадра сервера — иначе ответу негде было бы осесть.
    /// </remarks>
    public Http2StreamState RegisterPreSentRequest()
    {
        var state = new Http2StreamState(1, Volatile.Read(ref peerInitialWindowSize));
        streams[1] = state;
        nextStreamId = 3;
        return state;
    }

    /// <summary>
    /// Открывает поток, отправляет заголовки и, при наличии, тело запроса.
    /// </summary>
    /// <param name="headers">Заголовки, уже приведённые к виду HTTP/2.</param>
    /// <param name="body">Тело запроса; пустое значение означает запрос без тела.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Состояние открытого потока.</returns>
    public async ValueTask<Http2StreamState> SendRequestAsync(
        IReadOnlyList<KeyValuePair<string, string>> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(lifetime.IsCancellationRequested, this);
        if (terminalError is { } failure) throw new IOException("Сеанс HTTP/2 завершён", failure);
        if (isGoingAway) throw new IOException("Сервер прислал GOAWAY: новые запросы по этому соединению недопустимы");

        var state = await OpenStreamAsync(headers, endStream: body.IsEmpty, cancellationToken).ConfigureAwait(false);

        if (!body.IsEmpty)
        {
            try
            {
                await WriteDataAsync(state.Id, state, body, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Поток остался бы в таблице навсегда, а вызывающая сторона о нём уже не узнает:
                // исключение уходит вместо ссылки на состояние.
                streams.TryRemove(state.Id, out _);
                throw;
            }
        }

        return state;
    }

    /// <summary>
    /// Выделяет номер потока, кодирует заголовки и отправляет их.
    /// </summary>
    /// <remarks>
    /// Всё это делается под ОДНИМ удержанием замка записи, и на то три независимые причины.
    ///
    /// Первая: номера клиентских потоков обязаны возрастать в том порядке, в каком уходят кадры
    /// HEADERS. Выдай мы номер раньше замка, два параллельных запроса могли бы отправиться в
    /// обратном порядке — и сервер закрыл бы соединение с PROTOCOL_ERROR.
    ///
    /// Вторая: кодировщик HPACK хранит динамическую таблицу, а она общая на соединение и
    /// изменяется при каждом кодировании. Кодировать параллельно значит разрушить и таблицу, и
    /// согласие с представлением сервера — ответом будет COMPRESSION_ERROR.
    ///
    /// Третья: между HEADERS и его CONTINUATION нельзя вклинивать другие кадры.
    ///
    /// Под замком выполняется только кодирование и копирование в сокет: ожиданий сети здесь нет,
    /// а тело запроса уходит уже вне его, поэтому мультиплексирование не страдает.
    /// </remarks>
    private async ValueTask<Http2StreamState> OpenStreamAsync(
        IReadOnlyList<KeyValuePair<string, string>> headers,
        bool endStream,
        CancellationToken cancellationToken)
    {
        var block = new ArrayBufferWriter<byte>(512);

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Сервер вправе уменьшить размер динамической таблицы. Сигнал об этом обязан стоять в
            // начале блока заголовков, иначе сервер и мы разойдёмся в индексах.
            var requestedTableSize = Interlocked.Exchange(ref pendingEncoderTableSize, -1);
            if (requestedTableSize >= 0) encoder.UpdateDynamicTableSize(block, requestedTableSize);

            encoder.Encode(block, headers);

            // Клиентские потоки нумеруются нечётными числами по возрастанию, шаг ровно два.
            var streamId = nextStreamId;
            nextStreamId += 2;

            var state = new Http2StreamState(streamId, Volatile.Read(ref peerInitialWindowSize));
            streams[streamId] = state;

            try
            {
                await WriteHeaderFramesAsync(streamId, block.WrittenMemory, endStream, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                streams.TryRemove(streamId, out _);
                throw;
            }

            return state;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Снимает поток с учёта после завершения обмена по нему.
    /// </summary>
    /// <param name="streamId">Идентификатор потока.</param>
    /// <returns><see langword="true"/>, если поток был снят НЕЗАВЕРШЁННЫМ.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReleaseStream(int streamId)
    {
        if (!streams.TryRemove(streamId, out var state)) return false;

        // Незавершённый поток — тот, чьё тело сервер ещё досылает: именно о нём его и надо
        // уведомить кадром сброса.
        return !state.BodyCompleted.IsCompleted;
    }

    /// <summary>
    /// Пишет HEADERS и, при необходимости, CONTINUATION. Вызывается при удержанном замке записи.
    /// </summary>
    private async ValueTask WriteHeaderFramesAsync(int streamId, ReadOnlyMemory<byte> block, bool endStream, CancellationToken cancellationToken)
    {
        var maxFrame = Volatile.Read(ref peerMaxFrameSize);
        var first = true;
        var rest = block;

        while (true)
        {
            var chunk = rest[..Math.Min(rest.Length, maxFrame)];
            rest = rest[chunk.Length..];

            var last = rest.IsEmpty;
            byte flags = 0;
            if (last) flags |= 0x04;
            if (first && endStream) flags |= 0x01;

            var type = first ? Http2FrameType.Headers : Http2FrameType.Continuation;
            await WriteFrameCoreAsync(new Http2FrameHeader(chunk.Length, type, flags, streamId), chunk, cancellationToken).ConfigureAwait(false);

            if (last) break;
            first = false;
        }
    }

    /// <summary>
    /// Занимает место в окне отправки соединения.
    /// </summary>
    /// <param name="desired">Сколько байт хотелось бы отправить.</param>
    /// <returns>Сколько занять удалось; ноль — окно исчерпано.</returns>
    /// <remarks>
    /// Проверка и списание обязаны быть ОДНИМ действием, иначе несколько отправителей поделят
    /// один и тот же остаток между собой и все вместе выйдут за предел.
    /// </remarks>
    private int ReserveConnectionWindow(int desired)
    {
        while (true)
        {
            var available = Volatile.Read(ref connectionSendWindow);
            if (available <= 0) return 0;

            var take = Math.Min(desired, available);

            if (Interlocked.CompareExchange(ref connectionSendWindow, available - take, available) == available) return take;
        }
    }

    private async ValueTask WriteDataAsync(int streamId, Http2StreamState state, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var rest = body;

        while (!rest.IsEmpty)
        {
            // Подписываемся на возврат окна ДО проверки его размера. Обратный порядок теряет
            // пробуждение: приращение, пришедшее между проверкой и подпиской, осталось бы
            // незамеченным, и отправка встала бы до отмены.
            var wakeup = Volatile.Read(ref windowSignal).Task;

            if (terminalError is { } failure) throw new IOException("Сеанс HTTP/2 завершён", failure);

            // Отправлять больше, чем разрешают оба окна — соединения и потока, — нельзя: сервер
            // расценит это как ошибку протокола и оборвёт соединение.
            var allowed = Math.Min(state.SendWindow, Volatile.Read(ref peerMaxFrameSize));

            if (allowed <= 0)
            {
                await wakeup.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // ★ Окно СОЕДИНЕНИЯ занимается одним неделимым действием. Прежде оно читалось,
            // а списывалось отдельно — и между этими двумя шагами тот же остаток успевал
            // прочитать другой отправитель. Приходит WINDOW_UPDATE, будятся сразу все ждущие,
            // каждый видит одно и то же свободное место и берёт его целиком: суммарно на провод
            // уходит кратно больше разрешённого, сервер отвечает FLOW_CONTROL_ERROR и рвёт
            // соединение — вместе со всеми запросами, которые по нему шли.
            //
            // Проявляется тем вернее, чем больше параллельных отправок, то есть ровно там, где
            // мультиплексирование и нужно.
            var granted = ReserveConnectionWindow(Math.Min(rest.Length, allowed));

            if (granted <= 0)
            {
                await wakeup.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var chunk = rest[..granted];
            rest = rest[chunk.Length..];

            state.ConsumeSendWindow(chunk.Length);

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var flags = (byte)(rest.IsEmpty ? 0x01 : 0x00);
                await WriteFrameCoreAsync(new Http2FrameHeader(chunk.Length, Http2FrameType.Data, flags, streamId), chunk, cancellationToken).ConfigureAwait(false);

                // Пока мы держали замок, цикл чтения мог отложить служебные кадры — отправляем их
                // здесь же, не заставляя ждать следующей записи.
                await DrainControlFramesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
    }

    /// <summary>
    /// Будит отправителей, ожидающих возврата окна управления потоком.
    /// </summary>
    /// <remarks>
    /// Источник завершения заменяется целиком: так каждый ожидающий получает свежую подписку и
    /// повторно проверяет окна, а уже завершённый источник не «залипает» разрешением навсегда.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SignalWindowAvailable()
        => Interlocked.Exchange(ref windowSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    /// <summary>
    /// Пишет один кадр. Вызывается только при удержанном замке записи.
    /// </summary>
    /// <summary>
    /// Отправляет служебный кадр, НЕ дожидаясь замка записи.
    /// </summary>
    /// <param name="header">Заголовок кадра.</param>
    /// <param name="payload">Тело кадра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки или постановки в очередь.</returns>
    /// <remarks>
    /// ★ Цикл чтения НЕ ИМЕЕТ ПРАВА ждать замок записи. Замок держится всё время отправки в
    /// сокет, а отправка крупного тела упирается в буфер сокета и ждёт сеть — то есть цикл
    /// чтения вставал вместе с ней. А ведь именно он разбирает подтверждения и приращения окон,
    /// без которых отправка и не сдвинется: приём и передача принимались подпирать друг друга
    /// ровно там, где идёт крупная выгрузка одновременно с активным приёмом.
    ///
    /// Служебные кадры коротки и редки, поэтому здесь замок берётся ТОЛЬКО если он свободен
    /// сейчас же. Занят — кадр откладывается, и его отправит тот, кто замок держит, прямо перед
    /// освобождением. Порядок пользовательских кадров при этом не меняется.
    /// </remarks>
    private async ValueTask SendControlFrameAsync(Http2FrameHeader header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (await writeLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await WriteFrameCoreAsync(header, payload, cancellationToken).ConfigureAwait(false);
                await DrainControlFramesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }

            return;
        }

        // Тело копируем: буфер вызывающей стороны вернётся в пул сразу после выхода отсюда.
        controlFrames.Enqueue(new PendingControlFrame(header, payload.ToArray()));

        // Замок мог освободиться между неудачной попыткой и постановкой в очередь — тогда
        // разгружаем сами, иначе кадр остался бы ждать следующей записи.
        if (!await writeLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            await DrainControlFramesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Отправляет отложенные служебные кадры. Вызывается ТОЛЬКО под замком записи.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    private async ValueTask DrainControlFramesAsync(CancellationToken cancellationToken)
    {
        while (controlFrames.TryDequeue(out var pending))
            await WriteFrameCoreAsync(pending.Header, pending.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Отложенный служебный кадр.</summary>
    /// <param name="Header">Заголовок.</param>
    /// <param name="Payload">Тело, скопированное из буфера вызывающей стороны.</param>
    private readonly record struct PendingControlFrame(Http2FrameHeader Header, ReadOnlyMemory<byte> Payload);

    private async ValueTask WriteFrameCoreAsync(Http2FrameHeader header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Http2FrameHeader.Size + payload.Length);

        try
        {
            header.Write(buffer);
            payload.CopyTo(buffer.AsMemory(Http2FrameHeader.Size));

            await transport.WriteAsync(buffer.AsMemory(0, Http2FrameHeader.Size + payload.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var header = new byte[Http2FrameHeader.Size];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ReadExactAsync(header, cancellationToken).ConfigureAwait(false);
                var frame = Http2FrameHeader.Read(header);

                var payload = frame.Length > 0 ? ArrayPool<byte>.Shared.Rent(frame.Length) : [];

                try
                {
                    if (frame.Length > 0) await ReadExactAsync(payload.AsMemory(0, frame.Length), cancellationToken).ConfigureAwait(false);
                    await HandleFrameAsync(frame, payload.AsMemory(0, frame.Length), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (frame.Length > 0) ArrayPool<byte>.Shared.Return(payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение при закрытии сеанса.
        }
        catch (Exception error)
        {
            // Ошибка транспорта касается ВСЕХ потоков сразу: оставить их ждать значило бы обречь
            // каждый запрос на таймаут вместо немедленной внятной ошибки.
            terminalError = error;

            foreach (var state in streams.Values) state.Fail(error);

            SignalWindowAvailable();
        }
    }

    private async ValueTask HandleFrameAsync(Http2FrameHeader frame, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case Http2FrameType.Data:
                HandleData(frame, payload);
                await MaybeUpdateWindowsAsync(frame.StreamId, cancellationToken).ConfigureAwait(false);
                break;

            case Http2FrameType.Headers:
                HandleHeaders(frame, payload);
                break;

            case Http2FrameType.Continuation:
                HandleContinuation(frame, payload);
                break;

            case Http2FrameType.Settings:
                await HandleSettingsAsync(frame, payload, cancellationToken).ConfigureAwait(false);
                break;

            case Http2FrameType.WindowUpdate:
                HandleWindowUpdate(frame, payload);
                break;

            case Http2FrameType.Ping:
                await HandlePingAsync(frame, payload, cancellationToken).ConfigureAwait(false);
                break;

            case Http2FrameType.ResetStream:
                HandleResetStream(frame, payload);
                break;

            case Http2FrameType.GoAway:
                HandleGoAway(payload);
                break;

            case Http2FrameType.PushPromise:
                HandlePushPromise(frame, payload);
                break;

            default:
                // PRIORITY и неизвестные типы для клиента безразличны, но кадр всё равно должен
                // быть снят с провода — это уже сделано вызывающей стороной.
                break;
        }
    }

    private void HandleData(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        if (!streams.TryGetValue(frame.StreamId, out var state))
        {
            // ★ Поток брошен, но байты УЖЕ израсходовали окно приёма СОЕДИНЕНИЯ — сервер их
            // отправил и списал. Промолчав, мы окно не вернём, и оно будет медленно, запрос за
            // запросом, сходить к нулю: соединение из пула однажды просто перестанет получать
            // данные, без единой ошибки и без видимой причины.
            Interlocked.Add(ref connectionReceivedSinceUpdate, frame.Length);
            return;
        }

        var data = payload;

        // Кадр может быть дополнен: первый байт задаёт длину дополнения, которое в тело не входит.
        if (frame.Padded && data.Length > 0)
        {
            var padding = data.Span[0];
            data = data[1..];
            if (padding <= data.Length) data = data[..^padding];
        }

        if (!data.IsEmpty)
        {
            // Копируем: исходный буфер вернётся в пул сразу после обработки кадра.
            var copy = new byte[data.Length];
            data.CopyTo(copy);
            state.WriteBody(copy);
        }

        // ★ В окно возвращается ВСЯ длина кадра, а не только полезная часть. Управление потоком
        // считает байты кадра целиком, включая дополнение и байт его длины (RFC 9113, §6.9.1), —
        // сервер списал именно столько. Возвращая меньше, мы теряли по 1+padLength байт окна на
        // каждый дополненный кадр; дополнение же ставят ради защиты от анализа трафика, то есть
        // ровно там, где соединение живёт долго. Потеря копится молча, пока окно не сойдёт к
        // нулю и соединение не перестанет получать данные без единой ошибки.
        Interlocked.Add(ref connectionReceivedSinceUpdate, frame.Length);

        if (frame.EndStream) state.CompleteBody();
    }

    private void HandleHeaders(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        var block = payload;

        if (frame.Padded && block.Length > 0)
        {
            var padding = block.Span[0];
            block = block[1..];
            if (padding <= block.Length) block = block[..^padding];
        }

        // Поле приоритета идёт перед блоком заголовков и в HPACK не входит.
        if (frame.Priority && block.Length >= 5) block = block[5..];

        if (frame.EndHeaders)
        {
            DecodeHeaderBlock(frame.StreamId, block.Span, frame.EndStream);
            return;
        }

        // Блок продолжится кадрами CONTINUATION. Декодировать сейчас нельзя: HPACK — поточный
        // формат, и половина блока не является корректным блоком.
        pendingHeaderBlock = new ArrayBufferWriter<byte>(Math.Max(block.Length * 2, 512));
        pendingHeaderBlock.Write(block.Span);
        pendingHeaderStreamId = frame.StreamId;
        pendingHeaderEndStream = frame.EndStream;
    }

    /// <summary>
    /// Разбирает PUSH_PROMISE, который клиент не использует.
    /// </summary>
    /// <param name="frame">Заголовок кадра.</param>
    /// <param name="payload">Тело кадра.</param>
    /// <remarks>
    /// ★ Кадр «безразличен» только по содержанию, но НЕ по последствиям: он несёт блок HPACK, а
    /// HPACK — формат с общим на соединение состоянием. Не скормив блок декодировщику, мы
    /// расходимся с сервером в динамической таблице, и дальше ломаются ВСЕ последующие ответы —
    /// причём не отказом, а подменой имён и значений заголовков.
    ///
    /// Сам обещанный поток не нужен: браузеры объявляют ENABLE_PUSH=0, и мы объявляем тоже, так
    /// что доходить сюда серверу вообще не полагается. Раз дошло — разбираем блок и отказываемся
    /// от потока, а результат выбрасываем.
    /// </remarks>
    private void HandlePushPromise(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        var block = payload;

        if (frame.Padded && block.Length > 0)
        {
            var padding = block.Span[0];
            block = block[1..];
            if (padding <= block.Length) block = block[..^padding];
        }

        // Первые четыре байта — номер обещанного потока, в блок HPACK они не входят.
        if (block.Length < 4) return;

        var promised = (int)(BinaryPrimitives.ReadUInt32BigEndian(block.Span[..4]) & 0x7FFFFFFF);
        block = block[4..];

        try
        {
            // Результат не нужен — нужен сам факт разбора, синхронизирующий таблицу с сервером.
            _ = decoder.Decode(block.Span).Count();
        }
        catch (InvalidOperationException)
        {
            // Блок повреждён: таблица уже разошлась, и притворяться иначе смысла нет.
        }

        _ = Task.Run(() => RefusePromisedStreamAsync(promised), lifetime.Token);
    }

    /// <summary>
    /// Отказывается от обещанного сервером потока.
    /// </summary>
    /// <param name="promised">Номер обещанного потока.</param>
    /// <returns>Задача отправки.</returns>
    private async Task RefusePromisedStreamAsync(int promised)
    {
        try
        {
            await ResetStreamAsync(promised, Http2ErrorCode.RefusedStream, lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            // Соединение уже закрывается — отказываться не от чего.
        }
    }

    private void HandleContinuation(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        // CONTINUATION вне начатого блока — нарушение протокола со стороны сервера; молча
        // игнорируем кадр, потому что своей таблице сжатия он всё равно ничем не поможет.
        if (pendingHeaderBlock is null || pendingHeaderStreamId != frame.StreamId) return;

        // ★ Сборка блока ограничена по размеру. Без предела сервер — сломанный или враждебный —
        // мог слать поток кадров CONTINUATION без признака конца, а накопитель рос удвоением до
        // исчерпания памяти. Такой отказ уносит процесс целиком, вместе со всеми остальными
        // соединениями, и обработать его на месте нельзя.
        if (pendingHeaderBlock.WrittenCount + payload.Length > MaxHeaderBlockLength)
        {
            pendingHeaderBlock = null;
            throw new IOException($"Блок заголовков превысил {MaxHeaderBlockLength} Б");
        }

        pendingHeaderBlock.Write(payload.Span);

        if (!frame.EndHeaders) return;

        var assembled = pendingHeaderBlock.WrittenSpan;
        var streamId = pendingHeaderStreamId;
        var endStream = pendingHeaderEndStream;

        pendingHeaderBlock = null;
        pendingHeaderStreamId = -1;
        pendingHeaderEndStream = false;

        DecodeHeaderBlock(streamId, assembled, endStream);
    }

    /// <summary>
    /// Декодирует собранный блок заголовков и передаёт его потоку.
    /// </summary>
    /// <remarks>
    /// Декодирование выполняется ВСЕГДА, даже если поток уже неизвестен — например, отпущен
    /// вызывающей стороной или сброшен. Динамическая таблица HPACK общая на всё соединение, и
    /// пропуск одного блока сдвинул бы индексы для всех последующих ответов: сбой проявился бы
    /// далеко от места пропуска и выглядел бы как «испорченные заголовки на ровном месте».
    /// </remarks>
    private void DecodeHeaderBlock(int streamId, ReadOnlySpan<byte> block, bool endStream)
    {
        var decoded = decoder.Decode(block).ToArray();

        if (!streams.TryGetValue(streamId, out var state)) return;

        // Информационные ответы (1xx) не завершают заголовки: за ними придёт ещё один блок.
        // Публиковать их как окончательные значит отдать вызывающей стороне ответ 103 вместо 200.
        if (IsInformational(decoded)) return;

        state.CompleteHeaders(decoded);

        if (endStream) state.CompleteBody();
    }

    private static bool IsInformational(KeyValuePair<string, string>[] headers)
    {
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index];
            if (!string.Equals(header.Key, ":status", StringComparison.Ordinal)) continue;

            return header.Value.Length is 3 && header.Value[0] is '1';
        }

        return false;
    }

    /// <summary>
    /// Применяет новое начальное окно потоков, объявленное сервером.
    /// </summary>
    /// <param name="value">Значение настройки.</param>
    /// <remarks>
    /// Сервер вправе изменить его уже после открытия потоков, и разница применяется ко ВСЕМ
    /// существующим: спецификация требует именно этого, иначе наши окна разъедутся с серверными и
    /// отправка либо встанет, либо будет расценена как нарушение протокола.
    /// </remarks>
    private void ApplyPeerInitialWindowSize(uint value)
    {
        var updated = (int)Math.Min(value, int.MaxValue);
        var previous = Interlocked.Exchange(ref peerInitialWindowSize, updated);
        var delta = updated - previous;

        if (delta is 0) return;

        foreach (var state in streams.Values) state.IncreaseSendWindow(delta);

        if (delta > 0) SignalWindowAvailable();
    }

    private async ValueTask HandleSettingsAsync(Http2FrameHeader frame, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (frame.Ack) return;

        for (var offset = 0; offset + 6 <= payload.Length; offset += 6)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(payload.Span.Slice(offset, 2));
            var value = BinaryPrimitives.ReadUInt32BigEndian(payload.Span.Slice(offset + 2, 4));

            switch ((Http2SettingId)id)
            {
                case Http2SettingId.InitialWindowSize:
                    ApplyPeerInitialWindowSize(value);
                    break;

                case Http2SettingId.MaxConcurrentStreams:
                    // Предел сервера, а не наш: открыв больше, мы получим REFUSED_STREAM.
                    Interlocked.Exchange(ref peerMaxConcurrentStreams, (int)Math.Min(value, int.MaxValue));
                    break;

                case Http2SettingId.MaxFrameSize:
                    // Ограничивает НАШИ исходящие кадры. Значения вне допустимого диапазона
                    // спецификации игнорируем: следовать за некорректной настройкой опаснее, чем
                    // остаться на значении по умолчанию.
                    if (value is >= DefaultMaxFrameSize and <= 16777215)
                        Interlocked.Exchange(ref peerMaxFrameSize, (int)value);

                    break;

                case Http2SettingId.HeaderTableSize:
                    // Наш кодировщик не вправе рассчитывать на таблицу больше объявленной сервером.
                    // Применяется не здесь, а в начале следующего блока заголовков: сигнал об
                    // изменении размера обязан идти внутри блока и по порядку.
                    Interlocked.Exchange(ref pendingEncoderTableSize, (int)Math.Min(value, int.MaxValue));
                    break;

                default:
                    break;
            }
        }

        // Подтверждение обязательно и должно уходить сразу: сервер вправе не продолжать обмен
        // до его получения. Ждать замок при этом нельзя — мы в цикле чтения.
        await SendControlFrameAsync(new Http2FrameHeader(0, Http2FrameType.Settings, flags: 0x01, streamId: 0), ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    private void HandleWindowUpdate(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 4) return;

        var increment = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span[..4]) & 0x7FFFFFFF);
        if (increment <= 0) return;

        if (frame.StreamId is 0)
        {
            Interlocked.Add(ref connectionSendWindow, increment);
            SignalWindowAvailable();
            return;
        }

        if (streams.TryGetValue(frame.StreamId, out var state)) state.IncreaseSendWindow(increment);

        SignalWindowAvailable();
    }

    private async ValueTask HandlePingAsync(Http2FrameHeader frame, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (frame.Ack) return;

        // Ответ обязан содержать те же восемь байт, что пришли в запросе.
        await SendControlFrameAsync(new Http2FrameHeader(payload.Length, Http2FrameType.Ping, flags: 0x01, streamId: 0), payload, cancellationToken).ConfigureAwait(false);
    }

    private void HandleResetStream(Http2FrameHeader frame, ReadOnlyMemory<byte> payload)
    {
        if (!streams.TryRemove(frame.StreamId, out var state)) return;

        var code = (Http2ErrorCode)(payload.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(payload.Span[..4]) : 0);
        // ★ Тип отказа различается по коду: REFUSED_STREAM означает «этот запрос я даже не начал
        // обрабатывать, повтори» (RFC 9113, §8.7) — его безопасно повторить на новом соединении.
        // Прежде все сбросы приходили одним и тем же IOException, и отличить «повтори» от
        // «твой запрос отвергнут» вызывающая сторона не могла.
        state.Fail(code is Http2ErrorCode.RefusedStream
            ? new Http2StreamRefusedException($"Сервер отказал в потоке {frame.StreamId}: запрос не обработан и может быть повторён")
            : new IOException($"Сервер сбросил поток {frame.StreamId} с кодом {code} (0x{(uint)code:X})"));
    }

    private void HandleGoAway(ReadOnlyMemory<byte> payload)
    {
        var lastStreamId = payload.Length >= 4 ? (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span[..4]) & 0x7FFFFFFF) : 0;
        var code = payload.Length >= 8 ? BinaryPrimitives.ReadUInt32BigEndian(payload.Span.Slice(4, 4)) : 0;

        Volatile.Write(ref goAwayLastStreamId, lastStreamId);
        isGoingAway = true;

        // GOAWAY — это «больше не открывай», а не «всё пропало»: потоки с номером не выше
        // объявленного сервер обязуется довести до конца. Обрывать их значило бы терять готовые
        // ответы при каждом штатном закрытии соединения, а такие закрытия — норма, а не сбой.
        // ★ Поток с номером ВЫШЕ объявленного сервер обязуется не обрабатывать — он сам об этом
        // и сообщает (RFC 9113, §6.8). Значит запрос до обработки не дошёл и может быть повторён
        // на новом соединении; прежде он приходил обычным IOException, неотличимым от настоящего
        // сбоя, и терялся. Закрытие соединения при этом — норма, а не редкость: сервер так
        // разгружается или перезапускается.
        var error = new Http2StreamRefusedException(
            $"Сервер закрывает соединение HTTP/2 (код {code}); запрос не обработан и может быть повторён");

        foreach (var pair in streams)
        {
            if (pair.Key <= lastStreamId) continue;

            streams.TryRemove(pair.Key, out _);
            pair.Value.Fail(error);
        }

        // Ожидающих окна нужно разбудить: их поток мог остаться разрешённым, но соединение уже
        // не примет ничего нового, и висеть до отмены им незачем.
        SignalWindowAvailable();
    }

    private async ValueTask MaybeUpdateWindowsAsync(int streamId, CancellationToken cancellationToken)
    {
        // Окно приёма возвращаем порциями, а не на каждый кадр: приращение на каждые несколько
        // байт превратило бы приём в поток служебных кадров и заметно просело бы по скорости.
        var connectionPending = Volatile.Read(ref connectionReceivedSinceUpdate);
        var streamPending = streams.TryGetValue(streamId, out var state) ? state.ReceivedSinceUpdate : 0;

        if (connectionPending < WindowUpdateThreshold && streamPending < WindowUpdateThreshold) return;

        // Приращения окон уходят тем же путём, что и прочие служебные кадры: цикл чтения не
        // вправе ждать замок записи. Ирония в том, что именно эти кадры и разблокируют отправку,
        // которая замок держит, — ожидание здесь подпирало само себя.
        if (connectionPending >= WindowUpdateThreshold)
        {
            var amount = Interlocked.Exchange(ref connectionReceivedSinceUpdate, 0);
            if (amount > 0) await SendWindowUpdateAsync(streamId: 0, (uint)amount, cancellationToken).ConfigureAwait(false);
        }

        if (state is not null && streamPending >= WindowUpdateThreshold)
        {
            var amount = state.ExchangeReceived();
            if (amount > 0) await SendWindowUpdateAsync(streamId, (uint)amount, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Отправляет приращение окна, не дожидаясь замка записи.
    /// </summary>
    /// <param name="streamId">Поток или ноль для окна соединения.</param>
    /// <param name="increment">Приращение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    private ValueTask SendWindowUpdateAsync(int streamId, uint increment, CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, increment & 0x7FFFFFFF);

        return SendControlFrameAsync(new Http2FrameHeader(4, Http2FrameType.WindowUpdate, flags: 0, streamId), payload, cancellationToken);
    }


    /// <summary>
    /// Сообщает серверу, что поток брошен.
    /// </summary>
    /// <param name="streamId">Идентификатор потока.</param>
    /// <param name="errorCode">Код причины; по умолчанию отмена.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Без этого кадра сервер продолжает слать тело брошенного потока, расходуя окно соединения
    /// впустую, — а вернуть это окно уже некому.
    /// </remarks>
    public ValueTask ResetStreamAsync(int streamId, Http2ErrorCode errorCode, CancellationToken cancellationToken)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)errorCode);

        return SendControlFrameAsync(new Http2FrameHeader(4, Http2FrameType.ResetStream, flags: 0, streamId), payload, cancellationToken);
    }


    private async ValueTask ReadExactAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        var rest = destination;

        while (!rest.IsEmpty)
        {
            var read = await transport.ReadAsync(rest, cancellationToken).ConfigureAwait(false);
            if (read <= 0) throw new IOException("Соединение HTTP/2 закрыто удалённой стороной");

            rest = rest[read..];
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);

        if (readerLoop is not null)
        {
            try
            {
                // Это собственная задача сеанса, а не чужая: дожидаемся её завершения, чтобы цикл
                // чтения не пережил закрытие транспорта.
#pragma warning disable VSTHRD003
                await readerLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при закрытии.
            }
        }

        foreach (var state in streams.Values) state.Complete();
        streams.Clear();

        writeLock.Dispose();
        lifetime.Dispose();
    }
}
