using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Http3;
using Atom.Net.Https.Profiles;
using Atom.Net.Quic;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;
using Atom.Net.Udp;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Соединение HTTP/3 поверх собственного транспорта QUIC.
/// </summary>
/// <remarks>
/// Отличается от HTTP/2 не только протоколом, но и слоями. Мультиплексирование, управление
/// потоком, подтверждения и восстановление порядка здесь принадлежат QUIC
/// (<see cref="QuicConnection"/>), а разметка кадрами и сжатие заголовков — сеансу
/// (<see cref="Http3Session"/>). Соединению остаётся жизненный цикл и перевод между моделью
/// сообщений и потоками.
///
/// Транспорт — UDP, поэтому общий коннектор TCP здесь неприменим: рукопожатие TLS едет внутри
/// пакетов QUIC кадрами CRYPTO, а не поверх записей.
/// </remarks>
[SuppressMessage("Design", "MA0182:Internal type is apparently never used", Justification = "Создаётся обработчиком, когда запрошен HTTP/3.")]
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Соединение QUIC освобождается через Interlocked.Exchange в CloseAsync и Dispose.")]
internal sealed class Https3Connection : HttpsConnection
{
    [SuppressMessage("Major Code Smell", "S3459:Unassigned fields should be removed", Justification = "Изменяемая структура-счётчик, обновляется через Add после начала обмена.")]
    private Traffic traffic;
    private QuicConnection? quic;
    private Http3Session? session;
    private HttpsConnectionOptions options;
    private int activeStreams;
    private int isConnected;
    private int isDraining;
    private long createdTimestamp;
    private long lastActivityTimestamp;

    /// <summary>
    /// Параметры HTTP/3, наблюдаемые сервером.
    /// </summary>
    /// <remarks>
    /// Состав и порядок различаются между браузерами так же, как SETTINGS в HTTP/2, и потому
    /// принадлежат профилю, а не коду соединения.
    /// </remarks>
    /// <remarks>
    /// Значения сняты с настоящего Chrome через его журнал сети (событие HTTP3_SETTINGS_SENT):
    /// ёмкость таблицы QPACK 65536, предел списка заголовков 262144, сто заблокированных потоков
    /// и поддержка датаграмм. Объявлять ненулевую ёмкость таблицы можно только вместе с разбором
    /// встречного потока кодировщика — иначе сервер сошлётся на запись, которой у нас нет.
    /// </remarks>


    /// <inheritdoc/>
    public override Version Version => HttpVersion.Version30;

    /// <inheritdoc/>
    public override bool IsConnected => Volatile.Read(ref isConnected) is not 0;

    /// <inheritdoc/>
    public override bool IsSecure => true;

    /// <inheritdoc/>
    public override bool IsMultiplexing => true;

    /// <inheritdoc/>
    public override int ActiveStreams => Volatile.Read(ref activeStreams);

    /// <inheritdoc/>
    public override int MaxConcurrentStreams => (int)Math.Max(quic?.PeerParameters.InitialMaxStreamsBidi ?? 1, 1);

    /// <inheritdoc/>
    public override bool IsDraining => Volatile.Read(ref isDraining) is not 0;

    /// <inheritdoc/>
    public override IPEndPoint? LocalEndPoint => quic?.LocalEndPoint;

    /// <inheritdoc/>
    public override IPEndPoint? RemoteEndPoint => quic?.RemoteEndPoint;

    /// <inheritdoc/>
    public override long LastActivityTimestamp => Volatile.Read(ref lastActivityTimestamp);

    /// <inheritdoc/>
    public override long CreatedTimestamp => Volatile.Read(ref createdTimestamp);

    /// <inheritdoc/>
    public override Traffic Traffic => traffic;

    /// <inheritdoc/>
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

        if (!options.IsHttps) throw new NotSupportedException("HTTP/3 работает только поверх QUIC, то есть всегда защищённо");

        this.options = options;

        // Владение сокетом и соединением переходит полю this.quic либо освобождается в catch;
        // анализатор этой передачи через поле не видит.
#pragma warning disable CA2000
        var udp = new UdpStream(new UdpSettings());
        var openToken = HttpsTransportConnector.CreateOpenToken(options.ConnectTimeout, cancellationToken, out var timeoutCts);

        try
        {
            await udp.ConnectAsync(options.Host, options.Port, openToken).ConfigureAwait(false);

            var connection = new QuicConnection(
                udp,
                CreateTlsSettings(options),
                new QuicSettings(),
                options.ProfileQuicTransport ?? new QuicTransportParameters());

            try
            {
                await connection.ConnectAsync(openToken).ConfigureAwait(false);

                if (!string.Equals(connection.NegotiatedProtocol, "h3", StringComparison.Ordinal))
                    throw new NotSupportedException($"Сервер согласовал протокол '{connection.NegotiatedProtocol ?? "(нет)"}' вместо h3");

                var created = new Http3Session(
                    connection,
                    options.ProfileHttp3Settings ?? Http3ProfileCatalog.CreateChrome(),
                    options.ProfileHttp2Settings?.PseudoHeaderOrder ?? "masp");
                await created.StartAsync(openToken).ConfigureAwait(false);

                quic = connection;
                session = created;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            Volatile.Write(ref createdTimestamp, Stopwatch.GetTimestamp());
            Volatile.Write(ref isDraining, 0);
            Volatile.Write(ref isConnected, 1);
            Touch();
        }
#pragma warning restore CA2000
        catch (OperationCanceledException exception) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Сокет здесь ещё наш: соединение QUIC либо не создалось, либо освободило себя само.
            await udp.DisposeAsync().ConfigureAwait(false);

            throw new HttpsConnectTimeoutException($"Не удалось открыть соединение HTTP/3 с {options.Host}:{options.Port} за {options.ConnectTimeout}.", exception);
        }
        catch
        {
            // ★ Отказ ДО создания QuicConnection (не разрешилось имя, закрыт UDP) оставлял сокет
            // висеть до сборки мусора — финализатора у него нет. Попытки HTTP/3 по alt-svc
            // делаются оптимистично и часто, поэтому дескрипторы копились именно на этом пути.
            await udp.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<HttpsResponseMessage> SendAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();

        if (!IsConnected || session is null) throw new InvalidOperationException("Соединение не открыто");
        if (IsDraining) throw new InvalidOperationException("Соединение переведено в режим слива");

        var uri = request.RequestUri ?? throw new InvalidOperationException("В запросе не задан адрес");
        var authority = uri.IsDefaultPort ? uri.Host : string.Create(CultureInfo.InvariantCulture, $"{uri.Host}:{uri.Port}");

        Interlocked.Increment(ref activeStreams);

        try
        {
            var body = request.Content is null
                ? ReadOnlyMemory<byte>.Empty
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            var headers = CollectHeaders(request);
            var stream = await session.SendRequestAsync(request.Method.Method, authority, uri.PathAndQuery, headers, body, cancellationToken).ConfigureAwait(false);

            try
            {
                // ★ Ответ ограничен сроком этапа, как и в HTTP/1.1. Без него поток, по которому
                // сервер прислал заголовки и замолчал, не закрыв его, ждал бы до общего срока
                // всего запроса — и вместо названного этапа вызывающая сторона получала бы
                // невнятную «задача отменена».
                var (responseHeaders, payload) = await WaitWithTimeoutAsync(
                    session.ReadResponseAsync(stream, cancellationToken).AsTask(),
                    options.ResponseBodyTimeout,
                    "ответ",
                    cancellationToken).ConfigureAwait(false);

                TrackTraffic(sent: (ulong)body.Length, received: (ulong)payload.Length);
                Touch();

                return BuildResponse(request, responseHeaders, payload, Stopwatch.GetElapsedTime(started), options.AutoDecompression);
            }
            finally
            {
                // Снимаем поток с учёта в ЛЮБОМ случае, включая отмену и ошибку: соединение из
                // пула переживает десятки тысяч запросов, и каждый оставленный поток остаётся в
                // памяти вместе со своей очередью принятых данных.
                session.ReleaseStream(stream);
            }
        }
        finally
        {
            Interlocked.Decrement(ref activeStreams);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<bool> PingAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        return IsConnected && quic is { TerminalError: null };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void StartDrain() => Volatile.Write(ref isDraining, 1);

    /// <inheritdoc/>
    public override void Abort([AllowNull] Exception ex)
    {
        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);

        var current = Interlocked.Exchange(ref quic, value: null);
        var currentSession = Interlocked.Exchange(ref session, value: null);

        // Синхронный путь обрыва: блокировать поток на асинхронном освобождении нельзя, а циклы
        // чтения завершатся сами, как только закроется сокет.
        if (currentSession is not null) _ = Task.Run(async () => await currentSession.DisposeAsync().ConfigureAwait(false));
        if (current is not null) _ = Task.Run(async () => await current.DisposeAsync().ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public override async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        Volatile.Write(ref isDraining, 1);
        Volatile.Write(ref isConnected, 0);

        var current = Interlocked.Exchange(ref quic, value: null);
        var currentSession = Interlocked.Exchange(ref session, value: null);

        if (currentSession is not null) await currentSession.DisposeAsync().ConfigureAwait(false);
        if (current is not null) await current.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Собирает заголовки запроса для блока HTTP/3.
    /// </summary>
    /// <param name="request">Запрос.</param>
    /// <returns>Заголовки, допустимые к передаче.</returns>
    /// <remarks>
    /// Запрет на заголовки соединения в HTTP/3 тот же, что и в HTTP/2 (RFC 9114 §4.2), и цена
    /// пропуска та же: сервер вправе сбросить поток. Правило берётся общее — свой список здесь
    /// рано или поздно разошёлся бы с тем, что применяет HTTP/2.
    /// </remarks>
    private static List<KeyValuePair<string, string>> CollectHeaders(HttpsRequestMessage request)
    {
        var collected = new List<KeyValuePair<string, string>>(capacity: 16);

        // Значения берутся НЕРАЗОБРАННЫМИ, имена приводятся к нижнему регистру: и то и другое
        // требование протокола, а первое ещё и вопрос сходства с браузером.
        RequestHeaderReader.Collect(request, collected, lowercaseNames: true, ConnectionHeaderRules.IsProhibited);

        if (request.HeadersFormattingPolicy is not { } policy) return collected;

        // Порядок заголовков наблюдаем и задан профилем — тем же, что и для HTTP/2.
        var map = new Dictionary<string, string>(collected.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var header in collected) map[header.Key] = header.Value;

        return [.. policy.Format(map, HttpVersion.Version30, request.EffectiveKind, useCookieCrumbling: false)];
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

            // Распакованному телу противоречат оба заголовка: content-encoding описывает сжатие,
            // которого в нём уже нет, а content-length — длину сжатого.
            if (decoded && IsContentDescriptionHeader(header.Key)) continue;

            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (decoded) content.Headers.ContentLength = body.Length;

        response.StatusCode = status;
        response.Version = HttpVersion.Version30;
        response.Content = content;

        return response;
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
            // В HTTP/3, как и в HTTP/2, имена заголовков всегда в нижнем регистре.
            if (string.Equals(headers[index].Key, "content-encoding", StringComparison.Ordinal)) return headers[index].Value;
        }

        return null;
    }

    private static bool IsContentDescriptionHeader(string name)
        => string.Equals(name, "content-encoding", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ждёт задачу не дольше отведённого этапу срока.
    /// </summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="task">Ожидаемая задача.</param>
    /// <param name="timeout">Срок этапа; ноль или бесконечность снимают ограничение.</param>
    /// <param name="stage">Название этапа для сообщения об ошибке.</param>
    /// <param name="cancellationToken">Токен вызывающей стороны.</param>
    /// <returns>Результат задачи.</returns>
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
    /// Строит настройки рукопожатия для QUIC.
    /// </summary>
    /// <remarks>
    /// Приведение к виду, наблюдаемому у браузера поверх QUIC, целиком отдано
    /// <see cref="QuicTlsShaping"/> — там же изложены замеры, из которых оно выведено. Разница с
    /// профилем поверх TCP оказалась куда больше требований самого транспорта: сокращается список
    /// наборов шифров, исчезают подставные значения и часть расширений, а порядок перемешивается.
    ///
    /// Какому движку подражать, известно из параметров транспорта QUIC: у Chrome и Firefox
    /// расходятся и они, и форма рукопожатия, и берутся оба признака из одного профиля.
    /// </remarks>
    private static TlsSettings CreateTlsSettings(HttpsConnectionOptions options)
    {
        var profile = options.ProfileTlsSettings ?? new TlsSettings();
        var shape = options.ProfileQuicTransport?.Profile ?? QuicTransportProfile.Chromium;

        return QuicTlsShaping.Apply(profile, shape, options.Host) with
        {
            CheckCertificateRevocationList = options.CheckCertificateRevocationList,
            ServerCertificateValidationCallback = options.ServerCertificateValidationCallback,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrackTraffic(ulong sent, ulong received) => traffic.Add(sent, received);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Touch() => Volatile.Write(ref lastActivityTimestamp, Stopwatch.GetTimestamp());
}
