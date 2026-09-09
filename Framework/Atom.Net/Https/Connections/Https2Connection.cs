using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Http2;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Соединение HTTP/2 поверх собственного транспорта TLS.
/// </summary>
/// <remarks>
/// Мультиплексирование, управление потоком и разбор кадров вынесены в <see cref="Http2Session"/>;
/// здесь остаётся жизненный цикл соединения и преобразование между моделью сообщений
/// <see cref="HttpsRequestMessage"/> и потоками HTTP/2. Такое разделение позволяет проверять
/// мультиплексирование отдельно от сети и не смешивать две независимые области ответственности.
///
/// Соединение мультиплексирующее: параллельные запросы идут по одному TCP-каналу разными потоками,
/// поэтому <see cref="HasCapacity"/> не требует простоя, в отличие от HTTP/1.1.
/// </remarks>
[SuppressMessage("Design", "MA0182:Internal type is apparently never used", Justification = "Создаётся фабрикой соединений по согласованному протоколу.")]
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Транспорт и сеанс освобождаются через Interlocked.Exchange в CloseAsync, Abort и Dispose.")]
internal sealed class Https2Connection : HttpsConnection
{
    [SuppressMessage("Major Code Smell", "S3459:Unassigned fields should be removed", Justification = "Изменяемая структура-счётчик, обновляется через Add после начала обмена.")]
    private Traffic traffic;
    private Stream? transport;
    private TcpStream? socketTransport;
    private Http2Session? session;
    private HttpsConnectionOptions options;
    private Http.Http2Settings profile = Http2ProfileCatalog.CreateChrome();
    private int activeStreams;

    /// <summary>Сигнал освобождения места среди одновременных потоков.</summary>
    private TaskCompletionSource streamSlotSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int isConnected;
    private int isDraining;
    private long createdTimestamp;
    private long lastActivityTimestamp;

    /// <inheritdoc/>
    public override Version Version => HttpVersion.Version20;

    /// <inheritdoc/>
    public override bool IsConnected => Volatile.Read(ref isConnected) is not 0;

    /// <inheritdoc/>
    public override bool IsSecure => options.IsHttps;

    /// <inheritdoc/>
    public override bool IsMultiplexing => true;

    /// <inheritdoc/>
    public override int ActiveStreams => Volatile.Read(ref activeStreams);

    /// <summary>
    /// Предел одновременных потоков, объявленный сервером.
    /// </summary>
    /// <remarks>
    /// Берётся у сеанса, а не из профиля: профиль описывает НАШИ настройки, а ограничивает нас
    /// серверный SETTINGS. Спутать их — значит открыть больше потоков, чем разрешено, и получить
    /// REFUSED_STREAM вместо ответов ровно под нагрузкой, ради которой мультиплексирование и
    /// затевалось.
    /// </remarks>
    public override int MaxConcurrentStreams => session?.PeerMaxConcurrentStreams ?? 1;

    /// <summary>
    /// Соединение больше не принимает новые запросы.
    /// </summary>
    /// <remarks>
    /// Учитывает и GOAWAY: после него сервер обслужит уже открытые потоки, но новых не примет —
    /// значит, пул обязан перестать выдавать это соединение, продолжая при этом ждать ответы по
    /// начатым запросам.
    /// </remarks>
    /// <remarks>
    /// ★ Учитывается и СМЕРТЬ цикла чтения. Прежде она нигде не отражалась: цикл записывал
    /// терминальную ошибку у себя, валил открытые потоки — и на этом всё, а соединение
    /// продолжало числиться исправным и общим. Пул выдавал его КАЖДОМУ следующему запросу к
    /// этому узлу, и все они падали, пока живёт обработчик.
    ///
    /// Наступает это без всякой экзотики: балансировщик или nginx закрывает простаивающее
    /// соединение по истечении своего keep-alive обычным FIN, без GOAWAY. То есть узел
    /// становится недоступен навсегда после первой же паузы в работе.
    /// </remarks>
    public override bool IsDraining
        => Volatile.Read(ref isDraining) is not 0
        || session is { IsGoingAway: true }
        || session is { TerminalError: not null };

    /// <inheritdoc/>
    public override IPEndPoint? LocalEndPoint => socketTransport?.Socket.LocalEndPoint as IPEndPoint;

    /// <inheritdoc/>
    public override IPEndPoint? RemoteEndPoint => socketTransport?.Socket.RemoteEndPoint as IPEndPoint;

    /// <inheritdoc/>
    public override long LastActivityTimestamp => Volatile.Read(ref lastActivityTimestamp);

    /// <inheritdoc/>
    public override long CreatedTimestamp => Volatile.Read(ref createdTimestamp);

    /// <inheritdoc/>
    public override Traffic Traffic => traffic;

    /// <summary>
    /// Соединение готово принимать запросы, пока не исчерпан предел одновременных потоков.
    /// </summary>
    /// <remarks>
    /// В отличие от HTTP/1.1, занятость одним запросом здесь не мешает начать следующий: в этом и
    /// состоит смысл мультиплексирования.
    /// </remarks>
    public override bool HasCapacity => IsConnected && !IsDraining && ActiveStreams < MaxConcurrentStreams;

    /// <inheritdoc/>
    public override bool MatchesTarget(string host, int port, bool isHttps)
        => IsConnected
        && !IsDraining
        && options.Port == port
        && options.IsHttps == isHttps
        && string.Equals(options.Host, host, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override async ValueTask OpenAsync(HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            if (MatchesTarget(options.Host, options.Port, options.IsHttps)) return;
            throw new InvalidOperationException("Соединение уже открыто для другой цели");
        }

        if (!options.IsHttps) throw new NotSupportedException("HTTP/2 без TLS не поддерживается: браузеры используют только защищённый транспорт");

        var established = await HttpsTransportConnector.ConnectAsync(options, HttpsTransportConnector.Http2AndHttp11, cancellationToken).ConfigureAwait(false);
        earlyDataAccepted = established.EarlyDataAccepted;

        // Если сервер не выбрал h2, продолжать нельзя: говорить кадрами HTTP/2 по согласованному
        // http/1.1 значит получить немедленный разрыв вместо ответа.
        if (!established.IsHttp2)
        {
            await DisposeTransportAsync(established).ConfigureAwait(false);
            throw new NotSupportedException($"Сервер согласовал протокол '{established.NegotiatedProtocol ?? "(нет)"}' вместо h2");
        }

        try
        {
            await AdoptAsync(established, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeTransportAsync(established).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Принимает уже установленный транспорт с согласованным h2 и поднимает над ним сеанс.
    /// </summary>
    /// <param name="established">Установленный транспорт.</param>
    /// <param name="options">Параметры соединения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача, завершающаяся после обмена преамбулой и настройками.</returns>
    /// <remarks>
    /// Асинхронный, в отличие от одноимённого метода HTTP/1.1: до готовности соединения нужно
    /// отправить преамбулу и SETTINGS. Именно эти кадры и их порядок образуют наблюдаемый отпечаток
    /// HTTP/2, поэтому они принадлежат установке соединения, а не первому запросу.
    ///
    /// При ошибке транспорт НЕ освобождается: им продолжает владеть вызывающая сторона, которая
    /// его и установила. Освобождать здесь означало бы двойное освобождение на пути OpenAsync.
    /// </remarks>
    internal async ValueTask AdoptAsync(HttpsTransport established, HttpsConnectionOptions options, CancellationToken cancellationToken)
    {
        this.options = options;
        profile = options.ProfileHttp2Settings ?? Http2ProfileCatalog.CreateChrome();

        // При принятом 0-RTT преамбула и SETTINGS ушли в составе ранних данных, а поток 1
        // уже открытым: реестр обязан знать о нём до первого кадра сервера.
        var created = earlyDataAccepted && options.EarlyHeaderEncoder is { } seed
            ? new Http2Session(established.Transport, profile, connectionPrefaceAlreadySent: true, encoderSeed: seed)
            : new Http2Session(established.Transport, profile);

        try
        {
            await created.StartAsync(profile.ConnectionWindowIncrement, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await created.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        transport = established.Transport;
        socketTransport = established.Socket;
        session = created;

        if (earlyDataAccepted) preSentStream = created.RegisterPreSentRequest();

        Volatile.Write(ref createdTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref isDraining, 0);
        Volatile.Write(ref isConnected, 1);
        Touch();
    }

    private bool earlyDataAccepted;
    private Http2StreamState? preSentStream;

    /// <summary>
    /// Собирает 0-RTT payload: преамбула h2 с SETTINGS профиля и HEADERS(END_STREAM)
    /// для безопасного запроса (RFC 8446, §2.3; RFC 9113 — ранние кадры нумеруются с потока 1).
    /// </summary>
    /// <param name="headers">Список заголовков запроса (уже браузерного вида).</param>
    /// <param name="settings">Настройки HTTP/2 профиля.</param>
    /// <param name="connectionWindowIncrement">Приращение окна соединения профиля.</param>
    /// <returns>Payload и кодировщик, которым он закодирован: его состояние обязано стать
    /// состоянием сеанса, иначе динамическая таблица разойдётся с сервером.</returns>
    internal static (byte[] Payload, HPackEncoder Encoder) BuildEarlyRequestPayload(
        IReadOnlyList<KeyValuePair<string, string>> headers,
        Http.Http2Settings settings,
        uint connectionWindowIncrement)
    {
        var encoder = new HPackEncoder(4096);
        var block = new ArrayBufferWriter<byte>(512);
        encoder.Encode(block, headers);
        var headerBlock = block.WrittenMemory.ToArray();

        const byte HeadersEndStreamEndHeaders = 0x05;
        var frame = new Http2FrameHeader(headerBlock.Length, Http2FrameType.Headers, HeadersEndStreamEndHeaders, streamId: 1);
        var frameBytes = new byte[Http2FrameHeader.Size + headerBlock.Length];
        frame.Write(frameBytes);
        headerBlock.CopyTo(frameBytes, Http2FrameHeader.Size);

        var size = Http2Preface.GetRequiredSize(settings) + frameBytes.Length;
        var payload = new byte[size];
        var written = Http2Preface.Write(payload, settings, connectionWindowIncrement);
        frameBytes.CopyTo(payload, written);

        return (payload, encoder);
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> BuildHeaderList(
        HttpsRequestMessage request,
        int bodyLength,
        Http.Http2Settings settings)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("В запросе не задан адрес");
        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var path = uri.PathAndQuery;

        var headers = OrderHeaders(request);

        if (bodyLength > 0 && !headers.Exists(static header => string.Equals(header.Key, "content-length", StringComparison.OrdinalIgnoreCase)))
            headers.Add(new KeyValuePair<string, string>("content-length", bodyLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return Http2RequestHeaders.Build(request.Method.Method, authority, path, headers, settings);
    }

    private static async ValueTask DisposeTransportAsync(HttpsTransport established)
    {
        if (!ReferenceEquals(established.Transport, established.Socket))
            await established.Transport.DisposeAsync().ConfigureAwait(false);

        await established.Socket.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();

        if (!IsConnected || session is null) throw new InvalidOperationException("Соединение не открыто");
        if (IsDraining) throw new InvalidOperationException("Соединение переведено в режим слива");

        await ReserveStreamSlotAsync(cancellationToken).ConfigureAwait(false);

        Http2StreamState? stream = null;

        try
        {
            // Запрос уже улетел в составе 0-RTT: поток зарегистрирован, ожидаем только ответ.
            // Тело при этом не учитывается в трафике: оно уехало в составе ранних данных.
            var preSent = preSentStream is not null;
            var body = ReadOnlyMemory<byte>.Empty;

            if (!preSent)
            {
                body = request.Content is null
                    ? ReadOnlyMemory<byte>.Empty
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                var headers = BuildHeaders(request, body.Length);
                stream = await session.SendRequestAsync(headers, body, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                stream = preSentStream ?? throw new InvalidOperationException("0-RTT поток не зарегистрирован");
                preSentStream = null;
            }

            // ★ Этапные сроки применяются и здесь. Прежде они действовали ТОЛЬКО в HTTP/1.1, а
            // мультиплексируемые пути жили под одним лишь общим сроком запроса. Между тем именно
            // здесь зависание особенно вероятно: поток, по которому сервер прислал заголовки и
            // замолчал, не закрыв его, ждёт вечно — а таких узлов в сети хватает (ответ без
            // конца, потоковая выдача, защита от роботов). Один такой узел съедал весь бюджет
            // запроса и возвращал невнятную «задача отменена» вместо названного этапа.
            var responseHeaders = await WaitWithTimeoutAsync(
                stream.Headers,
                options.ResponseHeadersTimeout,
                "заголовки ответа",
                cancellationToken).ConfigureAwait(false);

            var payload = await ReadBodyWithinTimeoutAsync(stream, cancellationToken).ConfigureAwait(false);

            TrackTraffic(sent: (ulong)body.Length, received: (ulong)payload.Length);
            Touch();

            return BuildResponse(request, responseHeaders, payload, Stopwatch.GetElapsedTime(started), options.AutoDecompression);
        }
        finally
        {
            if (stream is not null && session is { } current) ReleaseStream(current, stream.Id);

            ReleaseStreamSlot();
        }
    }

    /// <summary>
    /// Занимает место среди одновременных потоков соединения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача ожидания свободного места.</returns>
    /// <remarks>
    /// ★ Предел одновременных потоков задаёт СЕРВЕР своими SETTINGS, и соблюдать его обязаны мы.
    /// Прежде единственной проверкой была HasCapacity в пуле — то есть задолго до открытия
    /// потока и без всякой атомарности: тысяча одновременных задач проходила её разом, все
    /// получали одно соединение и открывали потоки, а сервер отвечал RST_STREAM на всё сверх
    /// предела. Учёт при этом вёлся уже ПОСЛЕ выдачи соединения, так что второго рубежа не было.
    ///
    /// Теперь место занимается неделимо и ДО отправки заголовков, а лишние запросы ждут
    /// освобождения — вместо того чтобы получать отказ или плодить новые соединения.
    /// </remarks>
    private async ValueTask ReserveStreamSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            // Подписываемся на освобождение ДО проверки: обратный порядок теряет пробуждение.
            var wakeup = Volatile.Read(ref streamSlotSignal).Task;

            if (TryReserveStreamSlot()) return;

            if (!IsConnected || IsDraining) throw new InvalidOperationException("Соединение закрылось, пока ожидалось место для потока");

            await wakeup.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Пытается занять место среди одновременных потоков.
    /// </summary>
    /// <returns><see langword="true"/>, если место занято.</returns>
    private bool TryReserveStreamSlot()
    {
        var limit = MaxConcurrentStreams;

        while (true)
        {
            var current = Volatile.Read(ref activeStreams);
            if (current >= limit) return false;

            if (Interlocked.CompareExchange(ref activeStreams, current + 1, current) == current) return true;
        }
    }

    /// <summary>
    /// Освобождает место и будит ожидающих.
    /// </summary>
    private void ReleaseStreamSlot()
    {
        Interlocked.Decrement(ref activeStreams);

        var previous = Interlocked.Exchange(ref streamSlotSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    /// <summary>
    /// Снимает поток с учёта и, если он не был доведён до конца, уведомляет об этом сервер.
    /// </summary>
    /// <param name="current">Сессия соединения.</param>
    /// <param name="streamId">Идентификатор потока.</param>
    /// <remarks>
    /// Уведомление отправляется без ожидания и с погашением ошибок намеренно: этот путь
    /// исполняется в <see langword="finally"/>, в том числе при отмене, и сбой служебного кадра не должен
    /// подменять собой настоящую причину выхода. Не отправить его тоже нельзя — сервер иначе
    /// продолжит досылать тело в поток, который нас больше не интересует.
    /// </remarks>
    private static void ReleaseStream(Http2Session current, int streamId)
    {
        if (!current.ReleaseStream(streamId)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await current.ResetStreamAsync(streamId, Http2ErrorCode.Cancel, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
                // Соединение уже закрылось или закрывается — уведомлять некого и незачем.
            }
        });
    }

    /// <inheritdoc/>
    public override async ValueTask<bool> PingAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // Живость соединения определяется состоянием сеанса: если его цикл чтения завершился
        // ошибкой, соединение непригодно независимо от состояния сокета.
        return IsConnected && session is { TerminalError: null };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void StartDrain() => Volatile.Write(ref isDraining, 1);

    /// <inheritdoc/>
    public override void Abort([AllowNull] Exception ex)
    {
        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);

        var currentSession = Interlocked.Exchange(ref session, value: null);
        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocket = Interlocked.Exchange(ref socketTransport, value: null);

        // Синхронный путь обрыва. Освобождение сеанса запускаем без ожидания: блокировать поток
        // здесь нельзя — это классический источник взаимоблокировок, а цикл чтения всё равно
        // завершится сам, как только закроется транспорт.
        if (currentSession is not null) _ = Task.Run(async () => await currentSession.DisposeAsync().ConfigureAwait(false));

        currentTransport?.Dispose();
        currentSocket?.Dispose();
    }

    /// <inheritdoc/>
    public override async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);

        var currentSession = Interlocked.Exchange(ref session, value: null);
        var currentTransport = Interlocked.Exchange(ref transport, value: null);
        var currentSocket = Interlocked.Exchange(ref socketTransport, value: null);

        if (currentSession is not null) await currentSession.DisposeAsync().ConfigureAwait(false);
        if (currentTransport is not null) await currentTransport.DisposeAsync().ConfigureAwait(false);
        if (currentSocket is not null) await currentSocket.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Abort(ex: null);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private IReadOnlyList<KeyValuePair<string, string>> BuildHeaders(HttpsRequestMessage request, int bodyLength)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("В запросе не задан адрес");
        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
        var path = uri.PathAndQuery;

        // Значения берутся НЕРАЗОБРАННЫМИ: обычный обход отдаёт их поэлементно, и строка агента
        // уехала бы пятью заголовками, а accept — восемью (см. RequestHeaderReader).
        var headers = OrderHeaders(request);

        // Длину тела указываем явно: HTTP/2 не использует chunked-кодирование, и сервер полагается
        // на content-length там, где он применим.
        if (bodyLength > 0 && !headers.Exists(static header => string.Equals(header.Key, "content-length", StringComparison.OrdinalIgnoreCase)))
            headers.Add(new KeyValuePair<string, string>("content-length", bodyLength.ToString(CultureInfo.InvariantCulture)));

        return Http2RequestHeaders.Build(request.Method.Method, authority, path, headers, profile);
    }

    /// <summary>
    /// Ждёт задачу не дольше отведённого этапу срока.
    /// </summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="task">Ожидаемая задача.</param>
    /// <param name="timeout">Срок этапа; ноль или бесконечность снимают ограничение.</param>
    /// <param name="stage">Название этапа для сообщения об ошибке.</param>
    /// <param name="cancellationToken">Токен вызывающей стороны.</param>
    /// <returns>Результат задачи.</returns>
    /// <remarks>
    /// Отмена, пришедшая СНАРУЖИ, пробрасывается как есть: вызывающая сторона отказалась от
    /// запроса, и подменять это сообщением о сроке нельзя.
    /// </remarks>
    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout, string stage, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Не удалось получить {stage} за {timeout}.", exception);
        }
    }

    /// <summary>
    /// Забирает тело, не дожидаясь конца дольше отведённого срока.
    /// </summary>
    /// <param name="stream">Поток ответа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Тело: целиком либо столько, сколько успело прийти.</returns>
    /// <remarks>
    /// ★ Есть узлы, которые НИКОГДА не закрывают поток: заголовки и тело приходят за полсекунды,
    /// а признака конца нет вовсе. Замер: `www.ucoz.ru`, `kontur.ru`, `www.adriver.ru` отдают
    /// первый байт за 0,5 с и держат поток открытым бесконечно — на них и curl упирается в своё
    /// ограничение по времени, сколько его ни давай.
    ///
    /// Терять из-за этого ВЕСЬ ответ нельзя: данные уже получены, и браузер в такой обстановке
    /// давно показал бы страницу. Поэтому по истечении срока отдаём накопленное, а исключение
    /// бросаем только если не пришло вообще ничего — вот это уже настоящий отказ.
    ///
    /// Оборвать поток при этом обязательно: без RST_STREAM сервер продолжит слать тело в никуда,
    /// расходуя окно соединения, которое некому вернуть.
    /// </remarks>
    private async ValueTask<byte[]> ReadBodyWithinTimeoutAsync(Http2StreamState stream, CancellationToken cancellationToken)
    {
        var timeout = options.ResponseBodyTimeout;

        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await ReadBodyAsync(stream, cancellationToken).ConfigureAwait(false);

        try
        {
            return await ReadBodyAsync(stream, cancellationToken).AsTask().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            var partial = stream.TakeBody();

            if (partial.Length is 0)
                throw new TimeoutException($"Не удалось получить тело ответа за {timeout}.", exception);

            return partial;
        }
    }

    /// <summary>
    /// Дожидается конца тела ответа и забирает его.
    /// </summary>
    /// <param name="stream">Поток ответа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Тело ответа.</returns>
    /// <remarks>
    /// Данные накапливаются самим состоянием потока по мере прихода кадров, поэтому здесь
    /// остаётся только дождаться признака конца. Промежуточной очереди нет намеренно: она стоила
    /// бы полутора килобайт на каждый запрос и ничего не давала бы — тело всё равно возвращается
    /// целиком.
    /// </remarks>
    private static async ValueTask<byte[]> ReadBodyAsync(Http2StreamState stream, CancellationToken cancellationToken)
    {
        await stream.BodyCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);

        return stream.TakeBody();
    }

    private static HttpsResponseMessage BuildResponse(
        HttpsRequestMessage request,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        byte[] body,
        TimeSpan duration,
        bool autoDecompression)
    {
        var status = HttpStatusCode.OK;
        var response = new HttpsResponseMessage { Duration = duration, RequestMessage = request };

        var decoded = false;

        if (autoDecompression && ContentEncodingDecoder.TryDecode(body, FindContentEncoding(headers), out var inflated))
        {
            body = inflated;
            decoded = true;
        }

        var content = new ByteArrayContent(body);

        foreach (var header in headers)
        {
            if (header.Key.Length is 0) continue;

            if (header.Key[0] is ':')
            {
                if (string.Equals(header.Key, ":status", StringComparison.Ordinal)
                    && int.TryParse(header.Value, CultureInfo.InvariantCulture, out var code))
                {
                    status = (HttpStatusCode)code;
                }

                continue;
            }

            // Распаковав тело, мы обязаны убрать оба заголовка, которые теперь ему противоречат:
            // content-encoding описывает сжатие, которого в отданном теле уже нет, а
            // content-length — длину сжатого. Оставить их значит выдать вызывающей стороне
            // заведомо ложные сведения о содержимом. Браузер поступает так же.
            if (decoded && IsContentDescriptionHeader(header.Key)) continue;

            // Заголовки содержимого обязаны попасть в Content, иначе HttpContent их не увидит.
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (decoded) content.Headers.ContentLength = body.Length;

        response.StatusCode = status;
        response.Version = HttpVersion.Version20;
        response.Content = content;

        return response;
    }

    /// <summary>
    /// Собирает заголовки запроса в порядке, заданном профилем браузера.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <returns>Заголовки в порядке отправки.</returns>
    /// <remarks>
    /// ★ Порядок обычных заголовков наблюдаем ровно так же, как порядок расширений в ClientHello,
    /// и у браузеров он устойчив. Политика форматирования, описывающая его, в модуле была — и
    /// применялась ТОЛЬКО к HTTP/1.1, тогда как основной путь профилей идёт по HTTP/2. Замер
    /// настоящего Chrome на зеркале даёт
    /// <c lang="text">sec-ch-ua*, upgrade-insecure-requests, user-agent, accept, sec-fetch-*, accept-encoding,
    /// accept-language, priority</c>, а мы отправляли их в порядке добавления — начиная с
    /// user-agent и заканчивая подсказками клиента.
    ///
    /// Дробление cookie здесь НЕ включается: им занимается сборщик блока, и делать это дважды
    /// значит получить крошки от крошек.
    /// </remarks>
    private static List<KeyValuePair<string, string>> OrderHeaders(HttpsRequestMessage request)
    {
        var collected = new List<KeyValuePair<string, string>>(capacity: 16);
        RequestHeaderReader.Collect(request, collected, lowercaseNames: false);

        if (request.HeadersFormattingPolicy is not { } policy) return collected;

        var map = new Dictionary<string, string>(collected.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var header in collected) map[header.Key] = header.Value;

        return [.. policy.Format(map, HttpVersion.Version20, request.EffectiveKind, useCookieCrumbling: false)];
    }

    /// <summary>
    /// Находит объявленную кодировку содержимого.
    /// </summary>
    /// <param name="headers">Заголовки ответа.</param>
    /// <returns>Значение заголовка или <see langword="null"/>.</returns>
    private static string? FindContentEncoding(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            // В HTTP/2 имена заголовков всегда в нижнем регистре — так требует RFC 9113.
            if (string.Equals(headers[index].Key, "content-encoding", StringComparison.Ordinal)) return headers[index].Value;
        }

        return null;
    }

    private static bool IsContentDescriptionHeader(string name)
        => string.Equals(name, "content-encoding", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrackTraffic(ulong sent, ulong received) => traffic.Add(sent, received);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Touch() => Volatile.Write(ref lastActivityTimestamp, Stopwatch.GetTimestamp());
}
