using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Atom.Net.Tls;
using Atom.Net.Udp;

namespace Atom.Net.Quic;

/// <summary>
/// Клиентское соединение QUIC (RFC 9000/9001).
/// </summary>
/// <remarks>
/// Соединение делает три вещи одновременно, и разделение между ними существенно.
///
/// Первое — рукопожатие. Сообщения TLS едут кадрами CRYPTO на трёх разных уровнях шифрования, у
/// каждого своя нумерация пакетов и свои ключи. Сам TLS при этом обычный: им занимается
/// <see cref="Tls13ClientHandshake"/>, тот же, что работает поверх TCP.
///
/// Второе — надёжность поверх ненадёжного UDP: подтверждения, обнаружение потерь, повторная
/// передача. Повтор здесь не пересылка тех же байт, а упаковка потерянных данных в НОВЫЙ пакет с
/// новым номером: номер участвует в шифровании и повторяться не может.
///
/// Третье — потоки и управление ими. Данные могут прийти не по порядку, поэтому восстановлением
/// порядка занимается <see cref="QuicStream"/>, а не соединение.
/// </remarks>
[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Сокет и ключи освобождаются через Interlocked.Exchange в CloseAsync и Dispose.")]
[SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Настройки передаются по ссылке (in), что первичный конструктор не поддерживает.")]
public sealed class QuicConnection : IAsyncDisposable
{
    /// <summary>Наименьший размер датаграммы с пакетом Initial (RFC 9000, §14.1).</summary>
    private const int MinimumInitialDatagram = 1200;

    /// <summary>Размер буфера под исходящую датаграмму.</summary>
    private const int MaximumDatagram = 1452;

    /// <summary>
    /// Наибольший кусок данных потока в одном пакете.
    /// </summary>
    /// <remarks>
    /// Из размера датаграммы вычтены заголовок короткого пакета, заголовок кадра STREAM, место под
    /// подтверждения и метка подлинности. Запас намеренный: заголовки переменной длины, и
    /// упереться в границу пакета на отправке значит потерять запрос целиком.
    /// </remarks>
    private const int MaximumStreamChunk = 1300;

    private readonly UdpStream udp;
    private readonly QuicSettings settings;
    private readonly Tls13ClientHandshake handshake;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<ulong, QuicStream> streams = new();
    private readonly TaskCompletionSource handshakeComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly QuicPacketNumberSpace initialSpace = new();
    private readonly QuicPacketNumberSpace handshakeSpace = new();
    private readonly QuicPacketNumberSpace applicationSpace = new();

    private QuicKeys? initialWrite;
    private QuicKeys? initialRead;
    private QuicKeys? handshakeWrite;
    private QuicKeys? handshakeRead;
    private QuicKeys? applicationWrite;
    private QuicKeys? applicationRead;

    private byte[] destinationConnectionId = [];
    private byte[] sourceConnectionId = [];
    private byte[] retryToken = [];
    private byte[] cryptoBuffer = [];

    private readonly QuicLossRecovery loss = new();

    /// <summary>
    /// Пробуждение отправителей при возврате окна. Через смену источника завершения, а не опросом:
    /// опрос с паузой добавляет задержку каждой крупной отправке и жжёт такты на ровном месте.
    /// </summary>
    private TaskCompletionSource windowSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Сколько байт принято по всем потокам с момента последнего расширения окна.</summary>
    private long connectionReceivedSinceUpdate;

    /// <summary>Текущий предел данных соединения, объявленный нами.</summary>
    private long connectionReceiveLimit;

    private long connectionSendWindow;

    /// <summary>Отработавшие комплекты ключей: освобождаются вместе с соединением.</summary>
    /// <remarks>Их единицы даже на очень долгом соединении: смену ключей начинают редко.</remarks>
    private readonly System.Collections.Concurrent.ConcurrentBag<QuicKeys> retiredKeys = [];

    /// <summary>Сколько потоков каждого вида партнёр разрешил открыть за всё соединение.</summary>
    private long peerMaxBidirectionalStreams;
    private long peerMaxUnidirectionalStreams;

    /// <summary>Сколько байт данных потоков уже отправлено по соединению.</summary>
    /// <remarks>Нужен, чтобы из АБСОЛЮТНОГО предела MAX_DATA получить остаток окна.</remarks>
    private long connectionDataSent;
    private long nextBidirectionalStream;
    private long nextUnidirectionalStream = 2;
    private Task? readerLoop;
    private Task? probeLoop;
    private volatile Exception? terminalError;

    /// <summary>
    /// Создаёт соединение.
    /// </summary>
    /// <param name="udpStream">Сокет UDP, уже подключённый к серверу.</param>
    /// <param name="tlsSettings">Настройки TLS с параметрами транспорта в расширениях.</param>
    /// <param name="quicSettings">Настройки QUIC.</param>
    /// <param name="transportParameters">Параметры транспорта; идентификатор соединения заполняется соединением.</param>
    public QuicConnection(UdpStream udpStream, in TlsSettings tlsSettings, in QuicSettings quicSettings, in QuicTransportParameters transportParameters)
    {
        udp = udpStream;
        settings = quicSettings;
        parameters = transportParameters;
        tls = tlsSettings;
        handshake = new Tls13ClientHandshake(tlsSettings);
    }

    private readonly TlsSettings tls;
    private QuicTransportParameters parameters;

    /// <summary>Протокол, согласованный в ALPN.</summary>
    public string? NegotiatedProtocol => handshake.NegotiatedProtocol;

    /// <summary>Параметры транспорта, присланные сервером.</summary>
    public QuicTransportParameters PeerParameters { get; private set; }

    /// <summary>
    /// Потоки, открытые СЕРВЕРОМ.
    /// </summary>
    /// <remarks>
    /// В HTTP/3 по ним приходит служебная часть протокола: управляющий поток с параметрами и два
    /// потока QPACK. Транспорт их не толкует — он лишь отдаёт наверх, потому что смысл потоку
    /// придаёт прикладной протокол.
    /// </remarks>
    public System.Threading.Channels.ChannelReader<QuicStream> IncomingStreams => incomingStreams.Reader;

    /// <summary>Причина, по которой соединение перестало работать.</summary>
    public Exception? TerminalError => terminalError;

    /// <summary>
    /// Приёмник диагностики: получает по строке на каждое заметное событие соединения.
    /// </summary>
    /// <remarks>
    /// Без него отладка QUIC почти невозможна: обмен зашифрован с первого пакета, и снаружи
    /// видны только датаграммы UDP. Значение <see langword="null"/> отключает диагностику
    /// полностью, не стоя ничего на горячем пути.
    /// </remarks>
    public Action<string>? Diagnostics { get; set; }

    /// <summary>Завершено ли рукопожатие.</summary>
    public bool IsConnected => handshakeComplete.Task.IsCompletedSuccessfully && terminalError is null;

    /// <summary>Локальная точка подключения.</summary>
    public IPEndPoint? LocalEndPoint => udp.Socket.LocalEndPoint as IPEndPoint;

    /// <summary>Удалённая точка подключения.</summary>
    public IPEndPoint? RemoteEndPoint => udp.Socket.RemoteEndPoint as IPEndPoint;

    /// <summary>
    /// Выполняет рукопожатие.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача, завершающаяся после установки прикладных ключей.</returns>
    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        // Идентификатор адресата для первого пакета выбирается случайно: из него выводятся
        // начальные ключи, и сервер узнаёт его прямо из заголовка.
        destinationConnectionId = RandomNumberGenerator.GetBytes(8);

        // Собственный идентификатор — ПУСТОЙ, как у Chrome (замерено в его журнале сети:
        // initial_source_connection_id длиной ноль). Клиенту он не нужен: за маршрутизацию к нам
        // отвечает пара адресов UDP, а каждый байт здесь уезжает в КАЖДОМ пакете ответа.
        // ★ Длина СВОЕГО идентификатора соединения наблюдаема в каждом ответном пакете, а не
        // только в рукопожатии: она задаёт, сколько байт занимает адресат в коротком заголовке.
        // Chrome отправляет пустой, Firefox — трёхбайтовый, и это устойчивое различие.
        sourceConnectionId = parameters.Profile is QuicTransportProfile.Firefox
            ? RandomNumberGenerator.GetBytes(3)
            : [];

        PublishTransportParameters();

        var (client, server) = QuicInitialSecrets.Derive(settings.Version, destinationConnectionId);
        initialWrite = client;
        initialRead = server;

        readerLoop = Task.Run(() => ReadLoopAsync(lifetime.Token), CancellationToken.None);
        probeLoop = Task.Run(() => ProbeLoopAsync(lifetime.Token), CancellationToken.None);

        await SendClientHelloAsync(cancellationToken).ConfigureAwait(false);
        await handshakeComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Записывает параметры транспорта в расширение ClientHello.
    /// </summary>
    /// <remarks>
    /// Параметр <c lang="text">initial_source_connection_id</c> обязан ПОБАЙТНО совпадать с идентификатором
    /// источника в самом пакете Initial — так сервер убеждается, что заголовок пакета не подменён
    /// посредником. Заполнить его может только соединение, потому что идентификатор выбирает оно
    /// же; вызывающая сторона задаёт лишь пределы. Несовпадение сервер отвергает с ошибкой
    /// параметров транспорта — и это как раз тот случай, когда ошибка названа прямо.
    /// </remarks>
    private void PublishTransportParameters()
    {
        parameters = parameters with { InitialSourceConnectionId = sourceConnectionId };

        var encoded = new byte[512];
        var length = parameters.Write(encoded);

        foreach (var extension in tls.Extensions)
        {
            if (extension is not Tls.Extensions.QuicTransportParametersTlsExtension transport) continue;

            transport.Data = encoded.AsMemory(0, length);
            return;
        }

        throw new InvalidOperationException("В настройках TLS нет расширения quic_transport_parameters");
    }

    /// <summary>
    /// Открывает двунаправленный поток.
    /// </summary>
    /// <returns>Состояние потока.</returns>
    /// <remarks>
    /// Двунаправленные потоки клиента нумеруются числами, кратными четырём: два младших бита
    /// идентификатора кодируют инициатора и направление.
    /// </remarks>
    public QuicStream OpenBidirectionalStream()
    {
        var id = Interlocked.Add(ref nextBidirectionalStream, 4) - 4;

        EnsureStreamCredit(id, Volatile.Read(ref peerMaxBidirectionalStreams), "двунаправленных");

        return CreateStream((ulong)id, PeerParameters.InitialMaxStreamDataBidiRemote);
    }

    /// <summary>
    /// Открывает однонаправленный поток.
    /// </summary>
    /// <returns>Состояние потока.</returns>
    public QuicStream OpenUnidirectionalStream()
    {
        var id = Interlocked.Add(ref nextUnidirectionalStream, 4) - 4;

        EnsureStreamCredit(id, Volatile.Read(ref peerMaxUnidirectionalStreams), "однонаправленных");
        return CreateStream((ulong)id, PeerParameters.InitialMaxStreamDataUni);
    }

    /// <summary>
    /// Отправляет данные по потоку, соблюдая управление потоком.
    /// </summary>
    /// <param name="stream">Поток.</param>
    /// <param name="data">Данные.</param>
    /// <param name="fin">Закрыть ли поток после отправки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    public async ValueTask SendAsync(QuicStream stream, ReadOnlyMemory<byte> data, bool fin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var rest = data;

        do
        {
            if (terminalError is { } failure) throw new IOException("Соединение QUIC завершено", failure);

            // Оба окна — соединения и потока — ограничивают отправку независимо.
            // Подписываемся на возврат окна ДО проверки его размера: обратный порядок теряет
            // пробуждение, и отправка встанет до отмены.
            var wakeup = Volatile.Read(ref windowSignal).Task;

            // Кусок ограничен и окном ПОТОКА, и вместимостью пакета: датаграмма не резиновая.
            var allowed = (int)Math.Min(stream.SendWindow, MaximumStreamChunk);

            if (allowed <= 0)
            {
                await wakeup.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // ★ Место в окне СОЕДИНЕНИЯ занимается неделимо — та же ошибка, что была в HTTP/2:
            // между чтением остатка и его списанием другой поток успевал прочитать тот же
            // остаток, и после общего пробуждения по MAX_DATA все отправители брали одно и то же
            // свободное место. Итог — превышение предела и CONNECTION_CLOSE с кодом нарушения
            // управления потоком, то есть падение ВСЕХ запросов соединения разом.
            var granted = ReserveConnectionWindow(Math.Min(rest.Length, allowed));

            if (granted <= 0)
            {
                await wakeup.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var chunk = rest[..granted];
            rest = rest[chunk.Length..];

            await SendStreamFrameAsync(stream, chunk, fin && rest.IsEmpty, cancellationToken).ConfigureAwait(false);
        }
        while (!rest.IsEmpty);

        if (fin && data.IsEmpty) await SendStreamFrameAsync(stream, ReadOnlyMemory<byte>.Empty, fin: true, cancellationToken).ConfigureAwait(false);
    }

    private QuicStream CreateStream(ulong id, ulong sendWindow)
    {
        var stream = new QuicStream(id, sendWindow);
        streams[id] = stream;

        return stream;
    }

    /// <summary>
    /// Снимает завершённый поток с учёта соединения.
    /// </summary>
    /// <param name="stream">Поток, работа с которым окончена.</param>
    /// <remarks>
    /// ★ Без этого таблица потоков росла БЕЗ ГРАНИЦ: каждый запрос HTTP/3 оставлял в ней живой
    /// <see cref="QuicStream"/> вместе с его очередью принятых данных, а при обгоне пакетов — и
    /// со словарём отложенных кусков. Соединение из пула обслуживает десятки тысяч запросов, и
    /// каждый из них оставался в памяти навсегда. Проявляется это не отказом, а ростом
    /// потребления, то есть замечается позже всего.
    ///
    /// Вызывать обязана прикладная сторона: только она знает, что обмен по потоку окончен.
    /// </remarks>
    public void ReleaseStream(QuicStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        streams.TryRemove(stream.Id, out _);
    }

    /// <summary>
    /// Поднимает кредит потоков, если партнёр его увеличил.
    /// </summary>
    /// <param name="credit">Поле кредита.</param>
    /// <param name="limit">Присланный предел.</param>
    private static void RaiseStreamCredit(ref long credit, ulong limit)
    {
        while (true)
        {
            var current = Volatile.Read(ref credit);
            if ((long)limit <= current) return;

            if (Interlocked.CompareExchange(ref credit, (long)limit, current) == current) return;
        }
    }

    /// <summary>
    /// Проверяет, разрешил ли партнёр открыть поток с таким номером.
    /// </summary>
    /// <param name="id">Идентификатор нового потока.</param>
    /// <param name="credit">Текущий кредит потоков этого вида.</param>
    /// <param name="kind">Вид потоков — для сообщения об ошибке.</param>
    /// <remarks>
    /// Порядковый номер потока внутри его вида — это идентификатор, делённый на четыре. Открыть
    /// поток сверх кредита нельзя: партнёр обязан ответить обрывом соединения, и вместо одного
    /// отказавшего запроса упали бы все.
    /// </remarks>
    private static void EnsureStreamCredit(long id, long credit, string kind)
    {
        var ordinal = id / 4;

        if (ordinal < credit) return;

        throw new InvalidOperationException(
            $"Исчерпан кредит {kind} потоков QUIC: разрешено {credit}, запрошен номер {ordinal + 1}");
    }

    private int ReserveConnectionWindow(int desired)
    {
        while (true)
        {
            var available = Volatile.Read(ref connectionSendWindow);
            if (available <= 0) return 0;

            var take = (int)Math.Min(desired, available);

            if (Interlocked.CompareExchange(ref connectionSendWindow, available - take, available) == available)
            {
                Interlocked.Add(ref connectionDataSent, take);
                return take;
            }
        }
    }

    /// <summary>
    /// Применяет присланный партнёром предел данных соединения.
    /// </summary>
    /// <param name="limit">Абсолютный предел суммарно отправленного.</param>
    /// <remarks>
    /// ★ MAX_DATA несёт АБСОЛЮТНЫЙ предел (RFC 9000, §19.9), а не приращение и не остаток.
    /// Раньше он записывался прямо в остаток окна — то есть окно раздувалось до полного предела,
    /// сколько бы ни было уже отправлено. Ровно эта тонкость учтена этажом ниже, у окна потока,
    /// где из присланного предела вычитается собственное смещение.
    ///
    /// Предел ещё и не убывает: отставший или повторный кадр с меньшим значением игнорируется,
    /// иначе он отнял бы у отправителей уже выданное им место.
    /// </remarks>
    private void ApplyConnectionLimit(ulong limit)
    {
        while (true)
        {
            var current = Volatile.Read(ref connectionSendWindow);
            var updated = (long)limit - Volatile.Read(ref connectionDataSent);

            if (updated <= current) return;

            if (Interlocked.CompareExchange(ref connectionSendWindow, updated, current) == current) break;
        }

        SignalWindowAvailable();
    }

    private async ValueTask SendStreamFrameAsync(QuicStream stream, ReadOnlyMemory<byte> chunk, bool fin, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            var payload = ArrayPool<byte>.Shared.Rent(chunk.Length + 64);

            try
            {
                var writer = new QuicFrameWriter(payload.AsSpan(0, chunk.Length + 64));

                WritePendingAck(ref writer, applicationSpace);

                var offset = stream.SendOffset;
                if (!writer.WriteStream(stream.Id, offset, chunk.Span, fin)) throw new InvalidOperationException("Кадр STREAM не поместился в пакет");

                // Содержимое запоминаем ДО отправки: если пакет потеряется, повторять придётся
                // именно эти данные, а смещение потока к тому моменту уже уедет вперёд.
                pendingStreamFrames.Add(new QuicSentStreamFrame(stream.Id, offset, chunk.ToArray(), fin));

                var length = BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written));
                await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                applicationSpace.OnAckSent();
                stream.OnDataSent(chunk.Length);
            }
            finally
            {
                writeLock.Release();
                ArrayPool<byte>.Shared.Return(payload);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Отправляет первый пакет с ClientHello.
    /// </summary>
    /// <remarks>
    /// Датаграмма дополняется до 1200 байт: так проверяется, что путь пропускает пакеты нужного
    /// размера. Без дополнения сервер вправе вообще не отвечать, и выглядеть это будет как
    /// молчание сети.
    /// </remarks>
    private ValueTask SendClientHelloAsync(CancellationToken cancellationToken)
    {
        cryptoBuffer = handshake.BuildClientHello();
        return SendCryptoAsync(QuicEncryptionLevel.Initial, cryptoBuffer, cancellationToken);
    }

    /// <summary>
    /// Отправляет данные рукопожатия, дробя их по пакетам.
    /// </summary>
    /// <param name="level">Уровень шифрования.</param>
    /// <param name="data">Данные рукопожатия целиком.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Дробление обязательно, а не желательно. ClientHello современного браузера не помещается в
    /// один пакет: одна только доля ключа постквантовой группы занимает 1216 байт, а пакет
    /// ограничен путём — около 1200 байт вместе с заголовком и меткой подлинности. Отправив
    /// датаграмму крупнее, мы получим либо фрагментацию на уровне IP, либо молчание: такие
    /// датаграммы теряются или отвергаются.
    ///
    /// Каждая датаграмма с уровнем Initial дополняется до 1200 байт — этим клиент подтверждает,
    /// что путь пропускает пакеты нужного размера.
    /// </remarks>
    private async ValueTask SendCryptoAsync(QuicEncryptionLevel level, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var space = GetSpace(level);
        var isInitial = level is QuicEncryptionLevel.Initial;
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);
        var payload = ArrayPool<byte>.Shared.Rent(MaximumDatagram);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var offset = 0;

                do
                {
                    var keys = isInitial ? initialWrite! : handshakeWrite!;
                    var headerLength = EstimateLongHeaderLength(isInitial);

                    // Из бюджета датаграммы вычитаем заголовок пакета и метку подлинности; из
                    // остатка — заголовок самого кадра CRYPTO и место под подтверждения.
                    var budget = (isInitial ? MinimumInitialDatagram : MaximumDatagram) - headerLength - keys.Cipher.TagSize;
                    var writer = new QuicFrameWriter(payload.AsSpan(0, budget));

                    WritePendingAck(ref writer, space);

                    var reserve = 1 + QuicCodec.EncodedLength(space.CryptoOffset) + 4;
                    var chunk = Math.Min(data.Length - offset, Math.Max(writer.Remaining - reserve, 0));

                    if (chunk > 0)
                    {
                        if (!writer.WriteCrypto(space.CryptoOffset, data.Span.Slice(offset, chunk)))
                            throw new InvalidOperationException("Кадр CRYPTO не поместился в пакет");

                        space.CryptoOffset += (ulong)chunk;
                        offset += chunk;
                    }

                    var length = BuildLongHeaderPacket(buffer, payload.AsSpan(0, writer.Written), isInitial, padTo: isInitial ? MinimumInitialDatagram : 0);
                    await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                    space.OnAckSent();
                    Diagnostics?.Invoke($"отправлен {level}: {chunk} байт рукопожатия, датаграмма {length} байт");
                }
                while (offset < data.Length);
            }
            finally
            {
                writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>
    /// Считает длину длинного заголовка до начала полезной нагрузки.
    /// </summary>
    /// <param name="isInitial">Пакет уровня Initial несёт ещё и токен.</param>
    /// <returns>Число байт заголовка вместе с номером пакета.</returns>
    /// <remarks>
    /// Нужна ДО сборки нагрузки: от неё зависит, сколько данных туда влезет. Номер пакета
    /// считаем по наибольшей длине — четыре байта: занизив оценку, мы построили бы пакет, который
    /// не помещается в датаграмму.
    /// </remarks>
    private int EstimateLongHeaderLength(bool isInitial)
    {
        var length = 1 + 4 + 1 + destinationConnectionId.Length + 1 + sourceConnectionId.Length + 2 + 4;

        if (isInitial) length += QuicCodec.EncodedLength((ulong)retryToken.Length) + retryToken.Length;

        return length;
    }

    /// <summary>
    /// Наибольшее число диапазонов, попадающих в один кадр ACK.
    /// </summary>
    /// <remarks>
    /// Кадр всё равно ограничен размером пакета, а каждый диапазон занимает до восьми байт.
    /// Копия на стеке избавляет от выделения на КАЖДОЕ подтверждение — а их отправляется больше,
    /// чем всех остальных кадров вместе взятых.
    /// </remarks>
    private const int MaximumAckRanges = 32;

    private static void WritePendingAck(ref QuicFrameWriter writer, QuicPacketNumberSpace space)
    {
        if (!space.HasPendingAck) return;

        Span<(ulong Start, ulong End)> ranges = stackalloc (ulong Start, ulong End)[MaximumAckRanges];
        var count = space.CopyReceivedRanges(ranges);

        if (count is 0) return;

        _ = writer.WriteAck(ranges[..count], ackDelayMicroseconds: 0, ackDelayExponent: 3);
    }

    /// <summary>
    /// Собирает пакет с длинным заголовком: уровни Initial и Handshake.
    /// </summary>
    private int BuildLongHeaderPacket(Span<byte> datagram, ReadOnlySpan<byte> payload, bool initialType, int padTo)
    {
        var space = initialType ? initialSpace : handshakeSpace;
        var keys = initialType ? initialWrite! : handshakeWrite!;

        var packetNumber = space.AllocatePacketNumber();
        var packetNumberLength = QuicPacketProtection.GetPacketNumberLength(packetNumber, space.LargestAcknowledged);

        var offset = 0;

        // Первый байт: форма длинного заголовка, фиксированный бит, тип и длина номера пакета.
        datagram[offset++] = (byte)(0xC0 | (initialType ? 0x00 : 0x20) | (packetNumberLength - 1));

        BinaryPrimitives.WriteUInt32BigEndian(datagram[offset..], (uint)settings.Version);
        offset += 4;

        datagram[offset++] = (byte)destinationConnectionId.Length;
        destinationConnectionId.CopyTo(datagram[offset..]);
        offset += destinationConnectionId.Length;

        datagram[offset++] = (byte)sourceConnectionId.Length;
        sourceConnectionId.CopyTo(datagram[offset..]);
        offset += sourceConnectionId.Length;

        if (initialType)
        {
            if (!QuicCodec.TryWrite(datagram[offset..], (ulong)retryToken.Length, out var tokenWritten)) throw new InvalidOperationException("Не хватило места для длины токена");
            offset += tokenWritten;

            retryToken.CopyTo(datagram[offset..]);
            offset += retryToken.Length;
        }

        // Поле длины пишем двумя байтами всегда: реальная длина станет известна только после
        // сборки нагрузки, а её место в заголовке смещает и номер пакета, и точку выборки для
        // маски. Фиксированные два байта разрывают эту зависимость и покрывают любой наш пакет.
        var lengthPosition = offset;
        offset += 2;

        var packetNumberOffset = offset;
        QuicPacketProtection.WritePacketNumber(datagram[offset..], packetNumber, packetNumberLength);
        offset += packetNumberLength;

        payload.CopyTo(datagram[offset..]);
        var payloadLength = payload.Length;

        // Дополнение входит в ЭТОТ пакет, а не в датаграмму отдельно: иначе сервер увидит мусор
        // после конца пакета.
        if (padTo > 0)
        {
            var tagLength = keys.Cipher.TagSize;
            var current = offset + payloadLength + tagLength;

            if (current < padTo)
            {
                datagram.Slice(offset + payloadLength, padTo - current).Clear();
                payloadLength += padTo - current;
            }
        }

        var lengthValue = (ulong)(packetNumberLength + payloadLength + keys.Cipher.TagSize);
        WriteTwoByteVarint(datagram[lengthPosition..], lengthValue);

        var total = QuicPacketProtection.Protect(datagram, packetNumberOffset, packetNumberLength, payloadLength, packetNumber, keys);

        space.TrackSent(packetNumber, new QuicSentPacket(
            CryptoOffset: (long)space.CryptoOffset,
            CryptoData: ReadOnlyMemory<byte>.Empty,
            StreamFrames: [],
            IsAckEliciting: true,
            SentTimestamp: Stopwatch.GetTimestamp()));

        return total;
    }

    /// <summary>
    /// Собирает пакет с коротким заголовком: прикладной уровень.
    /// </summary>
    private int BuildShortHeaderPacket(Span<byte> datagram, ReadOnlySpan<byte> payload)
    {
        var keys = applicationWrite!;
        var packetNumber = applicationSpace.AllocatePacketNumber();
        var packetNumberLength = QuicPacketProtection.GetPacketNumberLength(packetNumber, applicationSpace.LargestAcknowledged);

        var offset = 0;

        // Первый байт короткого заголовка: форма, фиксированный бит, фаза ключа и длина номера.
        // Бит фазы (0x04) обязан отражать поколение ключей, которым пакет зашифрован: по нему
        // партнёр и узнаёт о смене.
        datagram[offset++] = (byte)(0x40 | (keyPhase ? 0x04 : 0x00) | (packetNumberLength - 1));

        destinationConnectionId.CopyTo(datagram[offset..]);
        offset += destinationConnectionId.Length;

        var packetNumberOffset = offset;
        QuicPacketProtection.WritePacketNumber(datagram[offset..], packetNumber, packetNumberLength);
        offset += packetNumberLength;

        payload.CopyTo(datagram[offset..]);
        var payloadLength = payload.Length;

        // Нагрузка обязана быть достаточной для выборки маски: четыре байта после начала номера
        // плюс шестнадцать байт выборки.
        var minimumPayload = 4 - packetNumberLength + QuicPacketProtection.SampleLength;
        if (payloadLength + keys.Cipher.TagSize < minimumPayload)
        {
            var padding = minimumPayload - payloadLength - keys.Cipher.TagSize;
            datagram.Slice(offset + payloadLength, padding).Clear();
            payloadLength += padding;
        }

        var total = QuicPacketProtection.Protect(datagram, packetNumberOffset, packetNumberLength, payloadLength, packetNumber, keys);

        applicationSpace.TrackSent(packetNumber, new QuicSentPacket(
            CryptoOffset: -1,
            CryptoData: ReadOnlyMemory<byte>.Empty,
            StreamFrames: pendingStreamFrames.Count is 0 ? [] : [.. pendingStreamFrames],
            IsAckEliciting: true,
            SentTimestamp: Stopwatch.GetTimestamp()));

        pendingStreamFrames.Clear();

        return total;
    }

    /// <summary>
    /// Записывает число переменной длины ровно двумя байтами.
    /// </summary>
    private static void WriteTwoByteVarint(Span<byte> destination, ulong value)
    {
        if (value > 0x3FFF) throw new InvalidOperationException("Длина пакета не умещается в два байта");

        // Разбиение длины на два разряда; проверяемая арифметика фреймворка иначе отвергает
        // усечение младшего байта.
        unchecked
        {
            destination[0] = (byte)(0x40 | (value >> 8));
            destination[1] = (byte)value;
        }
    }

    private QuicPacketNumberSpace GetSpace(QuicEncryptionLevel level) => level switch
    {
        QuicEncryptionLevel.Initial => initialSpace,
        QuicEncryptionLevel.Handshake => handshakeSpace,
        _ => applicationSpace,
    };

    /// <summary>
    /// Повторяет данные, на которые слишком долго нет подтверждений.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача цикла.</returns>
    /// <remarks>
    /// Обнаружение по подтверждениям работает, только когда подтверждения ПРИХОДЯТ: если потерян
    /// последний пакет полёта, подтверждать нечего, и обмен встаёт молча. Этот таймер и есть
    /// вторая половина обнаружения — он срабатывает по расчётному сроку, выведенному из
    /// измеренного времени оборота.
    ///
    /// Шаг опроса намеренно грубый: точность здесь не нужна, а частый опрос стоил бы такты на
    /// каждом соединении.
    /// </remarks>
    private async Task ProbeLoopAsync(CancellationToken cancellationToken)
    {
        var peerMaxAckDelay = TimeSpan.FromMilliseconds(25);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

                if (terminalError is not null) return;

                foreach (var level in AckLevels)
                {
                    var space = GetSpace(level);
                    if (!space.TryGetOldestUnacknowledged(out var oldest)) continue;
                    if (!loss.IsProbeDue(oldest.SentTimestamp, peerMaxAckDelay)) continue;

                    loss.OnProbeTimeout();
                    Diagnostics?.Invoke($"истёк срок подтверждения на уровне {level}, повторяем данные");

                    await ProbeLevelAsync(level, space, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение.
        }
        catch (Exception error)
        {
            terminalError = error;
        }
    }

    /// <summary>
    /// Повторяет содержимое самого старого неподтверждённого пакета уровня.
    /// </summary>
    /// <remarks>
    /// Для рукопожатия повторяется весь его поток данных с нуля: сервер сам отбросит уже
    /// принятое по смещениям. Для прикладного уровня повторяются кадры потоков, а если их не
    /// было — отправляется PING, чтобы вынудить подтверждение и получить новую оценку оборота.
    /// </remarks>
    private async ValueTask ProbeLevelAsync(QuicEncryptionLevel level, QuicPacketNumberSpace space, CancellationToken cancellationToken)
    {
        if (level is QuicEncryptionLevel.Initial && initialWrite is not null)
        {
            space.ClearSent();
            space.CryptoOffset = 0;

            await SendCryptoAsync(QuicEncryptionLevel.Initial, cryptoBuffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!space.TryGetOldestUnacknowledged(out var oldest)) return;

        if (oldest.StreamFrames.Count > 0)
        {
            foreach (var frame in oldest.StreamFrames) pendingRetransmission.Enqueue(frame);

            await RetransmitPendingAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendPingAsync(level, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет кадр PING, чтобы вынудить подтверждение.
    /// </summary>
    private async ValueTask SendPingAsync(QuicEncryptionLevel level, CancellationToken cancellationToken)
    {
        var keys = level switch
        {
            QuicEncryptionLevel.Initial => initialWrite,
            QuicEncryptionLevel.Handshake => handshakeWrite,
            _ => applicationWrite,
        };

        if (keys is null) return;

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);
        var payload = ArrayPool<byte>.Shared.Rent(64);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var writer = new QuicFrameWriter(payload.AsSpan(0, 64));
                if (!writer.WritePing()) return;

                var length = level is QuicEncryptionLevel.Application
                    ? BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written))
                    : BuildLongHeaderPacket(buffer, payload.AsSpan(0, writer.Written), level is QuicEncryptionLevel.Initial, padTo: 0);

                await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumDatagram + 512];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await udp.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0) continue;

                ProcessDatagram(buffer.AsSpan(0, read));
                await FlushPendingWorkAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение.
        }
        catch (Exception error)
        {
            terminalError = error;
            handshakeComplete.TrySetException(error);

            foreach (var stream in streams.Values) stream.Fail(error);
        }
    }

    /// <summary>
    /// Разбирает датаграмму, которая может содержать несколько склеенных пакетов.
    /// </summary>
    /// <remarks>
    /// Склейка — норма: сервер отправляет Initial и Handshake одной датаграммой, чтобы уложить
    /// рукопожатие в меньшее число оборотов. Остановиться на первом пакете значит потерять
    /// половину рукопожатия.
    /// </remarks>
    private void ProcessDatagram(Span<byte> datagram)
    {
        var offset = 0;

        while (offset < datagram.Length)
        {
            var consumed = ProcessPacket(datagram[offset..]);
            if (consumed <= 0) break;

            offset += consumed;
        }
    }

    private int ProcessPacket(Span<byte> packet)
    {
        if (packet.Length < 5) return 0;

        var isLongHeader = (packet[0] & 0x80) is not 0;

        return isLongHeader ? ProcessLongHeaderPacket(packet) : ProcessShortHeaderPacket(packet);
    }

    private int ProcessLongHeaderPacket(Span<byte> packet)
    {
        var version = QuicPacketProtection.ReadVersion(packet);
        var type = (packet[0] & 0x30) >> 4;

        var offset = 5;
        var destinationLength = packet[offset++];
        offset += destinationLength;

        var sourceLength = packet[offset++];
        var serverConnectionId = packet.Slice(offset, sourceLength).ToArray();
        offset += sourceLength;

        // Версия ноль означает согласование версий: сервер перечисляет поддерживаемые. Своей в
        // списке быть не может — иначе он бы просто ответил.
        if (version is 0) throw new NotSupportedException("Сервер не поддерживает предложенную версию QUIC");

        if (type is 3) return ProcessRetry(packet, serverConnectionId);

        if (type is 0)
        {
            // Токен уровня Initial клиенту не адресован, но его длину нужно пропустить.
            if (!QuicCodec.TryRead(packet[offset..], out var tokenLength, out var tokenRead)) return 0;
            offset += tokenRead + (int)tokenLength;
        }

        if (!QuicCodec.TryRead(packet[offset..], out var length, out var lengthRead)) return 0;
        offset += lengthRead;

        var packetEnd = offset + (int)length;
        if (packetEnd > packet.Length) return 0;

        var level = type is 0 ? QuicEncryptionLevel.Initial : QuicEncryptionLevel.Handshake;
        var keys = level is QuicEncryptionLevel.Initial ? initialRead : handshakeRead;

        if (keys is null)
        {
            Diagnostics?.Invoke($"пакет уровня {level} пропущен: ключи ещё не установлены");
            return packetEnd;
        }

        var space = GetSpace(level);

        // Опорным для восстановления усечённого номера служит наибольший ПРИНЯТЫЙ от партнёра,
        // а не подтверждённый им наш — см. QuicPacketNumberSpace.LargestReceived.
        if (!QuicPacketProtection.TryUnprotect(packet[..packetEnd], offset, (ulong)Math.Max(space.LargestReceived, 0), keys, out var packetNumber, out var payload))
        {
            Diagnostics?.Invoke($"пакет уровня {level} не расшифрован");
            return packetEnd;
        }

        Diagnostics?.Invoke($"принят {level} №{packetNumber}, нагрузка {payload.Length} байт");

        space.OnPacketReceived(packetNumber, ackEliciting: true);

        // Сервер выбирает СВОЙ идентификатор соединения в первом ответе; дальше пакеты
        // адресуются ему, а не тому случайному, что мы придумали для первого пакета.
        if (level is QuicEncryptionLevel.Initial && serverConnectionId.Length > 0) destinationConnectionId = serverConnectionId;

        HandleFrames(payload, level);

        return packetEnd;
    }

    /// <summary>
    /// Обрабатывает пакет Retry: сервер требует повторить рукопожатие с токеном.
    /// </summary>
    /// <remarks>
    /// Retry — это проверка обратного адреса: сервер не хочет тратить состояние на клиента,
    /// который мог подделать адрес отправителя. Начальные ключи после него выводятся заново, из
    /// НОВОГО идентификатора соединения, — забыть об этом значит получить нерасшифровываемые
    /// пакеты.
    /// </remarks>
    private int ProcessRetry(Span<byte> packet, byte[] serverConnectionId)
    {
        var offset = 5 + 1 + packet[5];
        offset += 1 + packet[offset];

        // Хвост в 16 байт — метка целостности Retry, токеном не является.
        var tokenLength = packet.Length - offset - 16;
        if (tokenLength <= 0) return packet.Length;

        retryToken = packet.Slice(offset, tokenLength).ToArray();
        destinationConnectionId = serverConnectionId;

        initialWrite?.Dispose();
        initialRead?.Dispose();

        var (client, server) = QuicInitialSecrets.Derive(settings.Version, destinationConnectionId);
        initialWrite = client;
        initialRead = server;

        retryPending = true;

        return packet.Length;
    }

    private bool retryPending;

    /// <summary>Текущая фаза ключа: ей помечаются отправляемые пакеты и по ней узнаётся смена.</summary>
    private bool keyPhase;

    /// <summary>Использует ли согласованный набор шифров ChaCha20 вместо AES-GCM.</summary>
    private bool UsesChaCha => handshake.NegotiatedCipherSuite is CipherSuite.TLS_CHACHA20_POLY1305_SHA256;

    /// <summary>Комплект следующего поколения ключей чтения, подготовленный заранее.</summary>
    private QuicKeys? applicationReadNext;

    private int ProcessShortHeaderPacket(Span<byte> packet)
    {
        if (applicationRead is null) return packet.Length;

        // В пакете ОТ СЕРВЕРА адресатом стоит НАШ идентификатор соединения, а не серверный.
        // Короткий заголовок не несёт длины поля — она известна только по тому, какой
        // идентификатор мы сами назвали при подключении. Взять здесь серверный значит промахнуться
        // мимо номера пакета на разницу длин: у Cloudflare идентификатор длиннее нашего, и
        // расшифровка проваливается молча, будто ключи неверны.
        var offset = 1 + sourceConnectionId.Length;

        // ★ Бит фазы ключа маскируется вместе с номером пакета, поэтому узнать фазу можно только
        // ПОСЛЕ снятия защиты заголовка. Пробуем текущим комплектом; если не вышло и фаза
        // сменилась — сервер начал смену ключей, и дальше всё пойдёт уже новым поколением.
        //
        // Без этого соединение умирает молча и не сразу: Google и Cloudflare инициируют смену на
        // долгоживущих соединениях по счётчику пакетов, и с этого момента НИ ОДИН пакет больше не
        // расшифровывается. В журнале остаётся только «прикладной пакет не расшифрован» — то же
        // самое сообщение, что и при неверных ключах.
        if (!TryUnprotectApplication(packet, offset, out var packetNumber, out var payload))
        {
            Diagnostics?.Invoke($"прикладной пакет не расшифрован, {packet.Length} байт");
            return packet.Length;
        }

        Diagnostics?.Invoke($"принят прикладной №{packetNumber}, нагрузка {payload.Length} байт");
        applicationSpace.OnPacketReceived(packetNumber, ackEliciting: true);
        HandleFrames(payload, QuicEncryptionLevel.Application);

        return packet.Length;
    }

    /// <summary>
    /// Снимает защиту с прикладного пакета, при необходимости переходя на новое поколение ключей.
    /// </summary>
    /// <param name="packet">Пакет.</param>
    /// <param name="offset">Смещение номера пакета.</param>
    /// <param name="packetNumber">Номер пакета.</param>
    /// <param name="payload">Расшифрованная нагрузка.</param>
    /// <returns><see langword="true"/>, если пакет разобран.</returns>
    /// <remarks>
    /// Следующее поколение выводится из ТЕКУЩЕГО секрета меткой <c lang="text">quic ku</c> (RFC 9001, §6.1),
    /// а не из материала рукопожатия. Ключ защиты заголовка при смене НЕ меняется — иначе бит
    /// фазы нечем было бы прочитать.
    /// </remarks>
    private bool TryUnprotectApplication(Span<byte> packet, int offset, out ulong packetNumber, out byte[] payload)
    {
        var largest = (ulong)Math.Max(applicationSpace.LargestReceived, 0);
        var first = packet[0];

        if (QuicPacketProtection.TryUnprotect(packet, offset, largest, applicationRead!, out packetNumber, out payload)) return true;

        // Заголовок мог остаться размаскированным после неудачной попытки — восстанавливаем его.
        packet[0] = first;

        applicationReadNext ??= DeriveNextGeneration(applicationRead!);

        if (!QuicPacketProtection.TryUnprotect(packet, offset, largest, applicationReadNext, out packetNumber, out payload))
        {
            packet[0] = first;
            return false;
        }

        Diagnostics?.Invoke("сервер сменил ключи: переходим на следующее поколение");

        // ★ Прежние комплекты НЕ освобождаются здесь. Смена ключей происходит в цикле чтения, а
        // ключами записи в тот же миг пользуются потоки запросов — они читают applicationWrite
        // под своим замком, о котором цикл чтения ничего не знает. Освобождение рвало объект
        // прямо под отправителем: шифр закрывался посреди работы, и запрос падал с обращением к
        // освобождённому объекту. Смена ключей — событие редкое (её начинает сервер на
        // долгоживущих соединениях), поэтому отработавшие комплекты просто откладываются и
        // освобождаются вместе с соединением.
        retiredKeys.Add(applicationRead!);
        applicationRead = applicationReadNext;
        applicationReadNext = null;

        // Свой комплект записи меняем следом: продолжать отправку прежней фазой после смены
        // партнёром допустимо недолго, и проще перейти сразу.
        if (applicationWrite is { } write)
        {
            var next = DeriveNextGeneration(write);
            applicationWrite = next;
            retiredKeys.Add(write);
            keyPhase = !keyPhase;
        }

        return true;
    }

    /// <summary>
    /// Выводит следующее поколение ключей из текущего секрета.
    /// </summary>
    /// <param name="current">Действующий комплект.</param>
    /// <returns>Комплект следующего поколения.</returns>
    private QuicKeys DeriveNextGeneration(QuicKeys current)
    {
        var next = Tls13KeySchedule.ExpandLabel(
            handshake.HashAlgorithm,
            current.Secret.Span,
            "quic ku",
            [],
            current.Secret.Length);

        return new QuicKeys(handshake.HashAlgorithm, next, handshake.AeadKeyLength, UsesChaCha);
    }

    private void HandleFrames(ReadOnlySpan<byte> payload, QuicEncryptionLevel level)
    {
        var reader = new QuicFrameReader(payload);

        while (reader.Read())
        {
            switch (reader.FrameType)
            {
                case (ulong)QuicFrameType.Crypto:
                    HandleCrypto(reader.Offset, reader.Data, level);
                    break;

                case (ulong)QuicFrameType.Ack:
                case (ulong)QuicFrameType.AckWithEcn:
                    HandleAck(GetSpace(level), reader.AckRanges, reader.LargestAcknowledged);
                    break;

                case (ulong)QuicFrameType.MaxData:
                    ApplyConnectionLimit(reader.MaximumData);
                    break;

                case (ulong)QuicFrameType.MaxStreamData:
                    if (streams.TryGetValue(reader.StreamId, out var limited)) limited.UpdateSendWindow(reader.MaximumData);
                    SignalWindowAvailable();
                    break;

                // ★ Кредит потоков КУМУЛЯТИВЕН: партнёр разрешает не «столько одновременно», а
                // «столько всего за соединение», и восполняет его этим кадром. Прежде кадр
                // читался разборщиком, но соединением НЕ обрабатывался вовсе — то есть кредит
                // не восполнялся никогда. На узле со скромным начальным пределом соединение
                // умирало после стольких же запросов, сколько тот предел, и лечилось только
                // переподключением.
                case (ulong)QuicFrameType.MaxStreamsBidi:
                    RaiseStreamCredit(ref peerMaxBidirectionalStreams, reader.MaximumData);
                    break;

                case (ulong)QuicFrameType.MaxStreamsUni:
                    RaiseStreamCredit(ref peerMaxUnidirectionalStreams, reader.MaximumData);
                    break;

                case (ulong)QuicFrameType.ConnectionCloseTransport:
                case (ulong)QuicFrameType.ConnectionCloseApplication:
                    HandleConnectionClose(reader.ErrorCode, System.Text.Encoding.UTF8.GetString(reader.Data));
                    break;

                case (ulong)QuicFrameType.HandshakeDone:
                    break;

                case (ulong)QuicFrameType.PathChallenge:
                    pendingPathResponse = reader.Data.ToArray();
                    break;

                // Сервер оборвал поток. Не сообщить об этом наверх значит оставить запрос ждать
                // данных, которых уже никогда не будет: он доживёт до общего таймаута, и причина
                // отказа — а сервер её прямо назвал кодом — потеряется.
                case (ulong)QuicFrameType.ResetStream:
                    if (streams.TryGetValue(reader.StreamId, out var reset))
                        reset.Fail(new IOException($"Сервер сбросил поток {reader.StreamId} QUIC с кодом 0x{reader.ErrorCode:X}"));

                    break;

                // Сервер просит прекратить отправку. Ответ по этому потоку уже не придёт, и
                // ожидание его бессмысленно ровно так же.
                case (ulong)QuicFrameType.StopSending:
                    if (streams.TryGetValue(reader.StreamId, out var stopped))
                        stopped.Fail(new IOException($"Сервер отказался принимать поток {reader.StreamId} QUIC с кодом 0x{reader.ErrorCode:X}"));

                    break;

                default:
                    if (reader.FrameType is >= (ulong)QuicFrameType.Stream and <= (ulong)QuicFrameType.StreamMax)
                    {
                        Diagnostics?.Invoke($"поток {reader.StreamId}: {reader.Data.Length} байт со смещения {reader.Offset}{(reader.IsFin ? ", конец" : string.Empty)}");
                        HandleStreamData(reader.StreamId, reader.Offset, reader.Data, reader.IsFin);
                    }
                    else
                    {
                        Diagnostics?.Invoke($"кадр 0x{reader.FrameType:X} пропущен");
                    }

                    break;
            }
        }
    }

    private byte[]? pendingPathResponse;

    /// <summary>
    /// Будит отправителей, ожидающих возврата окна управления потоком.
    /// </summary>
    /// <remarks>
    /// Источник завершения заменяется целиком: каждый ожидающий получает свежую подписку и
    /// повторно проверяет окна, а уже завершённый источник не «залипает» разрешением навсегда.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SignalWindowAvailable()
        => Interlocked.Exchange(ref windowSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    private void HandleStreamData(ulong streamId, ulong offset, ReadOnlySpan<byte> data, bool fin)
    {
        if (!streams.TryGetValue(streamId, out var stream))
        {
            stream = new QuicStream(streamId, PeerParameters.InitialMaxStreamDataUni);
            streams[streamId] = stream;

            // Младший бит идентификатора говорит, кто открыл поток. Открытые СЕРВЕРОМ отдаём
            // наверх: по ним приходят управляющий поток HTTP/3 и потоки QPACK, без разбора
            // которых динамическая таблица остаётся пустой, а сервер на неё ссылается.
            if ((streamId & 0x01) is not 0) incomingStreams.Writer.TryWrite(stream);
        }

        var delivered = stream.OnDataReceived(offset, data, fin);

        if (delivered <= 0) return;

        Interlocked.Add(ref connectionReceivedSinceUpdate, delivered);
        pendingStreamWindowUpdates.Enqueue(streamId);
    }

    private readonly ConcurrentQueue<ulong> pendingStreamWindowUpdates = new();

    private readonly System.Threading.Channels.Channel<QuicStream> incomingStreams =
        System.Threading.Channels.Channel.CreateUnbounded<QuicStream>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    /// <summary>
    /// Расширяет объявленные пределы приёма, когда израсходована заметная их часть.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Пределы приёма объявляются в параметрах транспорта один раз и не восстанавливаются сами.
    /// Не расширяя их, мы получаем остановку загрузки ровно на объявленном объёме — и выглядит
    /// это как обрыв на середине большого ответа, а не как исчерпание окна.
    ///
    /// Расширяем не на каждый килобайт: приращение на каждый кадр превратило бы приём в поток
    /// служебных пакетов.
    /// </remarks>
    private async ValueTask UpdateReceiveWindowsAsync(CancellationToken cancellationToken)
    {
        if (applicationWrite is null) return;

        var received = Volatile.Read(ref connectionReceivedSinceUpdate);
        var threshold = Math.Max(connectionReceiveLimit / 2, 65536);

        var streamsToUpdate = new List<ulong>();

        while (pendingStreamWindowUpdates.TryDequeue(out var streamId))
        {
            if (!streams.TryGetValue(streamId, out var stream)) continue;
            if (stream.Consumed < (ulong)Math.Max((long)parameters.InitialMaxStreamDataBidiLocal / 2, 65536)) continue;
            if (!streamsToUpdate.Contains(streamId)) streamsToUpdate.Add(streamId);
        }

        if (received < threshold && streamsToUpdate.Count is 0) return;

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);
        var payload = ArrayPool<byte>.Shared.Rent(512);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var writer = new QuicFrameWriter(payload.AsSpan(0, 512));

                if (received >= threshold)
                {
                    connectionReceiveLimit += received;
                    Interlocked.Add(ref connectionReceivedSinceUpdate, -received);

                    _ = writer.WriteMaxData((ulong)connectionReceiveLimit);
                }

                foreach (var streamId in streamsToUpdate)
                {
                    if (!streams.TryGetValue(streamId, out var stream)) continue;

                    _ = writer.WriteMaxStreamData(streamId, stream.Consumed + parameters.InitialMaxStreamDataBidiLocal);
                }

                if (writer.Written is 0) return;

                var length = BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written));
                await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    /// <summary>
    /// Обрабатывает подтверждение: снимает пакеты с учёта, уточняет оценку оборота и выявляет потери.
    /// </summary>
    /// <remarks>
    /// Оценка оборота считается по САМОМУ СВЕЖЕМУ из подтверждённых пакетов: старые дают завышенное
    /// значение, если подтверждение задержалось, и срок повторной передачи раздувается.
    /// </remarks>
    private void HandleAck(QuicPacketNumberSpace space, IReadOnlyList<(ulong Start, ulong End)> ranges, ulong largestAcknowledged)
    {
        var acknowledged = space.OnAckReceived(ranges);
        if (acknowledged.Count is 0) return;

        loss.OnAckReceived();

        var newest = acknowledged[0];
        foreach (var packet in acknowledged)
        {
            if (packet.SentTimestamp > newest.SentTimestamp) newest = packet;
        }

        loss.OnRttSample(Stopwatch.GetElapsedTime(newest.SentTimestamp), TimeSpan.Zero);

        var lost = space.TakeLostPackets(largestAcknowledged);
        if (lost.Count is 0) return;

        Diagnostics?.Invoke($"потеряно пакетов: {lost.Count}, повторяем содержимое");
        QueueRetransmission(lost);
    }

    /// <summary>
    /// Ставит содержимое потерянных пакетов в очередь на повторную отправку.
    /// </summary>
    /// <remarks>
    /// Отправка не делается здесь: разбор идёт по span-ам расшифрованного пакета, а отправка
    /// асинхронна. Очередь разбирается сразу после обработки датаграммы.
    /// </remarks>
    private void QueueRetransmission(IReadOnlyList<QuicSentPacket> lost)
    {
        foreach (var packet in lost)
        {
            foreach (var frame in packet.StreamFrames) pendingRetransmission.Enqueue(frame);
        }
    }

    private readonly ConcurrentQueue<QuicSentStreamFrame> pendingRetransmission = new();

    /// <summary>
    /// Кадры потоков, попавшие в собираемый сейчас пакет.
    /// </summary>
    /// <remarks>
    /// Заполняется под замком записи и тут же переезжает в учёт отправленного пакета, поэтому
    /// параллельного доступа к списку нет.
    /// </remarks>
    private readonly List<QuicSentStreamFrame> pendingStreamFrames = [];

    private void HandleConnectionClose(ulong errorCode, string reason)
    {
        // Коды от 0x100 — это перенесённые предупреждения TLS: 0x100 плюс номер alert. Показывать
        // их как «просто код» бесполезно, потому что причина при этом остаётся в TLS.
        var description = errorCode >= 0x100
            ? $"ошибка TLS, alert {errorCode - 0x100}"
            : $"код транспорта 0x{errorCode:X}";

        var error = new IOException($"Сервер закрыл соединение QUIC: {description}. {reason}");

        terminalError = error;
        handshakeComplete.TrySetException(error);

        foreach (var stream in streams.Values) stream.Fail(error);
    }

    /// <summary>
    /// Скармливает данные CRYPTO движку рукопожатия и продвигает уровни ключей.
    /// </summary>
    /// <param name="offset">Смещение данных в потоке рукопожатия своего уровня.</param>
    /// <param name="data">Данные кадра.</param>
    /// <param name="level">Уровень шифрования, на котором кадр пришёл.</param>
    /// <remarks>
    /// Кадры собираются в поток своего уровня, а движку отдаются ЦЕЛЫЕ сообщения: в QUIC
    /// сообщение рукопожатия свободно пересекает границу пакета, и разбирать половину нельзя.
    /// </remarks>
    private void HandleCrypto(ulong offset, ReadOnlySpan<byte> data, QuicEncryptionLevel level)
    {
        var stream = level is QuicEncryptionLevel.Initial ? initialCrypto : handshakeCrypto;
        stream.Write(offset, data);

        while (stream.TryReadMessage(out var message))
        {
            if (level is QuicEncryptionLevel.Initial)
            {
                handshake.ProcessServerHello(message.Span);
                InstallHandshakeKeys();
                Diagnostics?.Invoke($"ServerHello разобран, набор {handshake.NegotiatedCipherSuite}, группа {handshake.NegotiatedGroup}");
                continue;
            }

            if (!handshake.ProcessHandshakeMessages(message.Span)) continue;

            // Параметры читаем ДО установки прикладных ключей: из них берётся окно данных
            // соединения, и с нулевым окном первая же отправка встанет навсегда.
            ReadPeerParameters();
            InstallApplicationKeys();
            finishedPending = true;
            Diagnostics?.Invoke($"полёт рукопожатия разобран, alpn={handshake.NegotiatedProtocol ?? "(нет)"}");
        }
    }

    private readonly QuicCryptoStream initialCrypto = new();
    private readonly QuicCryptoStream handshakeCrypto = new();
    private bool finishedPending;

    private void InstallHandshakeKeys()
    {
        var useChaCha = handshake.NegotiatedCipherSuite is CipherSuite.TLS_CHACHA20_POLY1305_SHA256;

        handshakeWrite = new QuicKeys(handshake.HashAlgorithm, handshake.ClientHandshakeSecret.Span, handshake.AeadKeyLength, useChaCha);
        handshakeRead = new QuicKeys(handshake.HashAlgorithm, handshake.ServerHandshakeSecret.Span, handshake.AeadKeyLength, useChaCha);
    }

    private void InstallApplicationKeys()
    {
        var useChaCha = handshake.NegotiatedCipherSuite is CipherSuite.TLS_CHACHA20_POLY1305_SHA256;

        applicationWrite = new QuicKeys(handshake.HashAlgorithm, handshake.ClientApplicationSecret.Span, handshake.AeadKeyLength, useChaCha);
        applicationRead = new QuicKeys(handshake.HashAlgorithm, handshake.ServerApplicationSecret.Span, handshake.AeadKeyLength, useChaCha);

        Volatile.Write(ref connectionSendWindow, (long)PeerParameters.InitialMaxData);
        Volatile.Write(ref peerMaxBidirectionalStreams, (long)PeerParameters.InitialMaxStreamsBidi);
        Volatile.Write(ref peerMaxUnidirectionalStreams, (long)PeerParameters.InitialMaxStreamsUni);
        connectionReceiveLimit = (long)parameters.InitialMaxData;

        Diagnostics?.Invoke($"прикладные ключи установлены, окно соединения {PeerParameters.InitialMaxData}");
    }

    /// <summary>
    /// Выполняет действия, отложенные разбором пакетов: повтор после Retry, Finished, ответ на проверку пути.
    /// </summary>
    /// <remarks>
    /// Отправка вынесена из разбора намеренно: разбор идёт по span-ам расшифрованного пакета, а
    /// отправка асинхронна, и смешивать их значит держать буфер живым дольше, чем нужно.
    /// </remarks>
    private async ValueTask FlushPendingWorkAsync(CancellationToken cancellationToken)
    {
        if (retryPending)
        {
            retryPending = false;

            initialSpace.CryptoOffset = 0;
            await SendCryptoAsync(QuicEncryptionLevel.Initial, cryptoBuffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (finishedPending)
        {
            finishedPending = false;

            // Сервер вправе попросить сертификат клиента и здесь; ответить обязаны, пусть и
            // пустым списком, иначе полёт неполон — см. Tls13ClientHandshake.BuildClientCertificate.
            if (handshake.BuildClientCertificate() is { } certificate)
                await SendCryptoAsync(QuicEncryptionLevel.Handshake, certificate, cancellationToken).ConfigureAwait(false);

            await SendCryptoAsync(QuicEncryptionLevel.Handshake, handshake.BuildClientFinished(), cancellationToken).ConfigureAwait(false);
            // Рукопожатие завершено: транскрипт освобождает свои буферы.
            handshake.ReleaseTranscript();
            handshakeComplete.TrySetResult();
        }

        if (pendingPathResponse is { } challenge)
        {
            pendingPathResponse = null;
            await SendPathResponseAsync(challenge, cancellationToken).ConfigureAwait(false);
        }

        await SendPendingAcksAsync(cancellationToken).ConfigureAwait(false);
        await RetransmitPendingAsync(cancellationToken).ConfigureAwait(false);
        await UpdateReceiveWindowsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Повторно отправляет данные потоков из потерянных пакетов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Данные едут в НОВОМ пакете с новым номером и с тем же смещением потока: получатель
    /// уложит их на прежнее место сам. Повторить старый пакет целиком нельзя — номер участвует
    /// в шифровании, и его повтор разрушил бы защиту.
    /// </remarks>
    private async ValueTask RetransmitPendingAsync(CancellationToken cancellationToken)
    {
        if (pendingRetransmission.IsEmpty || applicationWrite is null) return;

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);

        try
        {
            while (pendingRetransmission.TryDequeue(out var frame))
            {
                await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                var payload = ArrayPool<byte>.Shared.Rent(frame.Data.Length + 64);

                try
                {
                    var writer = new QuicFrameWriter(payload.AsSpan(0, frame.Data.Length + 64));

                    if (!writer.WriteStream(frame.StreamId, frame.Offset, frame.Data.Span, frame.IsFin)) continue;

                    pendingStreamFrames.Add(frame);

                    var length = BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written));
                    await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // ★ Буфер кадра возвращается в пул. Прежде он арендовался на КАЖДУЮ повторную
                    // передачу и не возвращался вовсе: на канале с потерями пул постепенно
                    // опустошался, и выделения начинали идти мимо него — то есть тем хуже, чем
                    // хуже связь, ровно когда запас производительности и нужен.
                    ArrayPool<byte>.Shared.Return(payload);
                    writeLock.Release();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Отправляет накопленные подтверждения отдельными пакетами.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Подтверждать нужно САМОСТОЯТЕЛЬНО, не дожидаясь своих данных. До проверки адреса клиента
    /// сервер не вправе отправить больше, чем втрое превышает полученное от нас, — и если мы
    /// молчим, он упирается в этот предел и замолкает сам. Снаружи это выглядит как зависшее
    /// рукопожатие, хотя обе стороны исправны.
    /// </remarks>
    private async ValueTask SendPendingAcksAsync(CancellationToken cancellationToken)
    {
        foreach (var level in AckLevels)
        {
            var space = GetSpace(level);
            if (!space.HasPendingAck || space.ReceivedRangeCount is 0) continue;

            var keys = level switch
            {
                QuicEncryptionLevel.Initial => initialWrite,
                QuicEncryptionLevel.Handshake => handshakeWrite,
                _ => applicationWrite,
            };

            if (keys is null) continue;

            await SendAckOnlyPacketAsync(level, space, cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly QuicEncryptionLevel[] AckLevels =
    [
        QuicEncryptionLevel.Initial,
        QuicEncryptionLevel.Handshake,
        QuicEncryptionLevel.Application,
    ];

    private async ValueTask SendAckOnlyPacketAsync(QuicEncryptionLevel level, QuicPacketNumberSpace space, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);
        var payload = ArrayPool<byte>.Shared.Rent(512);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var writer = new QuicFrameWriter(payload.AsSpan(0, 512));
                WritePendingAck(ref writer, space);

                if (writer.Written is 0) return;

                var length = level is QuicEncryptionLevel.Application
                    ? BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written))
                    : BuildLongHeaderPacket(buffer, payload.AsSpan(0, writer.Written), level is QuicEncryptionLevel.Initial, padTo: 0);

                await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                space.OnAckSent();
            }
            finally
            {
                writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private void ReadPeerParameters()
    {
        // Параметры сервера приходят в EncryptedExtensions; движок рукопожатия отдаёт их сырым
        // расширением, разбор которого принадлежит транспорту, а не TLS.
        if (handshake.PeerQuicTransportParameters.IsEmpty) return;

        PeerParameters = QuicTransportParameters.Read(handshake.PeerQuicTransportParameters.Span);
    }

    private async ValueTask SendPathResponseAsync(byte[] challenge, CancellationToken cancellationToken)
    {
        if (applicationWrite is null) return;

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumDatagram);

        try
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var payload = ArrayPool<byte>.Shared.Rent(64);
                var writer = new QuicFrameWriter(payload.AsSpan(0, 64));

                _ = writer.WritePathResponse(challenge);

                var length = BuildShortHeaderPacket(buffer, payload.AsSpan(0, writer.Written));
                await udp.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);

        foreach (var loop in new[] { readerLoop, probeLoop })
        {
            if (loop is null) continue;

            try
            {
#pragma warning disable VSTHRD003
                await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при закрытии.
            }
        }

        incomingStreams.Writer.TryComplete();

        foreach (var stream in streams.Values) stream.Complete();
        streams.Clear();

        initialWrite?.Dispose();
        initialRead?.Dispose();
        handshakeWrite?.Dispose();
        handshakeRead?.Dispose();
        applicationWrite?.Dispose();
        applicationRead?.Dispose();

        // Комплекты, отработавшие после смены ключей, освобождаются здесь: делать это в момент
        // смены нельзя — ими ещё пользуются отправители.
        while (retiredKeys.TryTake(out var retired)) retired.Dispose();

        handshake.Dispose();
        writeLock.Dispose();
        lifetime.Dispose();

        await udp.DisposeAsync().ConfigureAwait(false);
    }
}
