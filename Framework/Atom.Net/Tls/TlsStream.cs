#pragma warning disable CA2213

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Tls;

/// <summary>
/// Представляет базовую реализацию потока TLS.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="TlsStream"/>.
/// </remarks>
/// <param name="settings">Настройки TLS.</param>
/// <param name="stream">Сетевой поток.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract class TlsStream([NotNull] NetworkStream stream, in TlsSettings settings) : Stream
{
    private readonly Stream transportStream = stream;

    /// <summary>
    /// AEAD шифр для чтения, устанавливается после handshake.
    /// </summary>
    private IAeadCipher? aeadRead;

    /// <summary>
    /// AEAD шифр для записи, устанавливается после handshake.
    /// </summary>
    private IAeadCipher? aeadWrite;

    /// <summary>
    /// Фиксированный IV (TLS 1.3) для чтения.
    /// </summary>
    private byte[]? ivRead;

    /// <summary>
    /// Фиксированный IV (TLS 1.3) для записи.
    /// </summary>
    private byte[]? ivWrite;

    /// <summary>
    /// Версия протокола в заголовке TLS record.
    /// Для TLS 1.2 — 0x0303, для TLS 1.3 (legacy_record_version) — 0x0301.
    /// </summary>
    protected virtual ushort RecordLayerVersion => 0x0303;

    /// <summary>
    /// Настройки TLS.
    /// </summary>
    public TlsSettings Settings { get; protected set; } = settings;

    /// <summary>
    /// Протокол, выбранный сервером в ALPN, либо <see langword="null"/>, если ALPN не согласован.
    /// </summary>
    /// <remarks>
    /// Свойство живёт в базовом классе намеренно: выбор прикладного протокола — свойство
    /// рукопожатия, а не его версии. Держать его только в TLS 1.3 значило бы, что HTTP/2 поверх
    /// TLS 1.2 (а такие серверы есть) остаётся недостижимым, и вызывающей стороне пришлось бы
    /// приводить поток к конкретному типу, чтобы узнать результат согласования.
    /// </remarks>
    public string? NegotiatedProtocol { get; protected set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <summary>
    /// Происходит в процессе обработки Handshake-записи.
    /// </summary>
    /// <param name="payload">Raw payload без заголовка record.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract ValueTask<bool> OnHandshakeRecordAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>
    /// Формирует ClientHello (включая все расширения, алг. и пр.).
    /// </summary>
    /// <param name="destination">Назначение записи.</param>
    /// <returns>Фактический размер ClientHello.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract int BuildClientHello(Span<byte> destination);

    /// <summary>
    /// Вызывается при завершении рукопожатия для установки AEAD и IV.
    /// </summary>
    /// <param name="read"></param>
    /// <param name="write"></param>
    /// <param name="ivReadBytes"></param>
    /// <param name="ivWriteBytes"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetTrafficKeys(IAeadCipher read, IAeadCipher write, byte[] ivReadBytes, byte[] ivWriteBytes)
    {
        aeadRead = read;
        aeadWrite = write;
        ivWrite = ivWriteBytes;
        ivRead = ivReadBytes;
    }

    /// <summary>
    /// Отправляет одну TLS-запись (Handshake/Alert/ChangeCipherSpec) без шифрования (до установки ключей).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask SendPlainRecordAsync(TlsContentType type, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var header = new TlsRecordHeader(type, RecordLayerVersion, (ushort)data.Length);
        var buf = ArrayPool<byte>.Shared.Rent(5 + data.Length);

        try
        {
            header.Write(buf.AsSpan());
            data.CopyTo(buf.AsMemory(5, data.Length));
            await transportStream.WriteAsync(buf.AsMemory(0, 5 + data.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask WriteTransportAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => transportStream.WriteAsync(data, cancellationToken);

    /// <summary>
    /// Читает следующую TLS-запись (заголовок+пейлоад). Возвращает пейлоад в пуловском буфере.
    /// </summary>
    /// <remarks>
    /// Вариант для тех, кому запись НУЖНА: её отсутствие здесь — отказ. Закрытие партнёром
    /// сообщается отдельным типом <see cref="TlsConnectionClosedException"/>, потому что для
    /// прикладного чтения это не отказ, а конец данных, и различать два события по тексту
    /// сообщения нельзя. Кому конец потока допустим — берёт <see cref="TryReadRecordAsync"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask<(TlsRecordHeader header, byte[] payload)> ReadRecordAsync(CancellationToken cancellationToken)
    {
        var (header, payload, endOfStream) = await TryReadRecordAsync(cancellationToken).ConfigureAwait(false);

        if (endOfStream) throw new TlsConnectionClosedException();

        return (header, payload!);
    }

    /// <summary>
    /// Читает следующую запись, отличая конец потока от повреждения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись либо признак конца потока.</returns>
    /// <remarks>
    /// ★ Обрыв НА ГРАНИЦЕ записи и обрыв ПОСРЕДИ неё — разные события, и смешивать их нельзя.
    /// Партнёр, закрывший соединение обычным FIN без предупреждения close_notify, — обыденность:
    /// так поступают балансировщики, серверы в стиле HTTP/1.0 и часть потоковых обработчиков.
    /// Для ответа без длины и без кусочной передачи именно закрытие соединения и означает конец
    /// тела — а мы бросали исключение и теряли уже полученные данные целиком.
    ///
    /// Обрыв внутри начатой записи — по-прежнему ошибка: там половина сообщения, и делать вид,
    /// что всё в порядке, нельзя.
    /// </remarks>
    protected async ValueTask<(TlsRecordHeader Header, byte[]? Payload, bool EndOfStream)> TryReadRecordAsync(CancellationToken cancellationToken)
    {
        var hdr = ArrayPool<byte>.Shared.Rent(5);

        try
        {
            var read = 0;

            while (read < 5)
            {
                var got = await transportStream.ReadAsync(hdr.AsMemory(read, 5 - read), cancellationToken).ConfigureAwait(false);

                if (got <= 0)
                {
                    if (read is 0) return (default, null, true);

                    throw new InvalidOperationException("Разрыв соединения посреди заголовка записи TLS");
                }

                read += got;
            }

            var header = TlsRecordHeader.Read(hdr);

            var buf = ArrayPool<byte>.Shared.Rent(header.Length);
            await ReadExactAsync(buf.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);

            return (header, buf, false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(hdr);
        }
    }

    /// <summary>
    /// Читает ровно count байт в указанный span. Без лишних аллокаций.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask ReadExactAsync(Memory<byte> dst, CancellationToken cancellationToken)
    {
        var mem = dst;

        while (!mem.IsEmpty)
        {
            var got = await transportStream.ReadAsync(mem, cancellationToken).ConfigureAwait(false);
            if (got <= 0) throw new InvalidOperationException("Разрыв соединения при чтении TLS");
            mem = mem[got..];
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer) => transportStream.Read(buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => transportStream.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer) => transportStream.Write(buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => transportStream.WriteAsync(buffer, cancellationToken);

    /// <summary>
    /// Выполняет рукопожатие.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract ValueTask HandshakeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет рукопожатие.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask HandshakeAsync()
    {
        if (Settings.HandshakeTimeout <= TimeSpan.Zero || Settings.HandshakeTimeout == Timeout.InfiniteTimeSpan)
        {
            await HandshakeAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        using var timeoutCts = new CancellationTokenSource(Settings.HandshakeTimeout);
        await HandshakeAsync(timeoutCts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);

        aeadWrite?.Dispose();
        aeadRead?.Dispose();

        if (ivRead is not null) ArrayPool<byte>.Shared.Return(ivRead, clearArray: true);
        if (ivWrite is not null) ArrayPool<byte>.Shared.Return(ivWrite, clearArray: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Расшифровывает содержимое записи alert в читаемый вид.
    /// </summary>
    /// <param name="body">Тело записи: уровень и описание.</param>
    /// <returns>Описание для сообщения об ошибке.</returns>
    /// <remarks>
    /// Существует ради одного: alert без описания превращает внятную причину в загадку. Ровно на
    /// этом была потеряна отладка ALPS — сервер присылал «уровень 2, описание 10», то есть
    /// фатальный unexpected_message, прямо называющий проблему, а в исключение попадала строка
    /// «Получен TLS alert», по которой нельзя понять решительно ничего.
    /// </remarks>
    protected static string DescribeAlert(ReadOnlySpan<byte> body)
    {
        if (body.Length < 2) return "тело alert не разобрано";

        var level = (TlsAlertLevel)body[0];
        var description = (TlsAlertDescription)body[1];

        return $"уровень {level} ({body[0]}), описание {description} ({body[1]})";
    }
}
