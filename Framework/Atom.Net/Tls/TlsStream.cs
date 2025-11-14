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
    private readonly Stream stream = stream;

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
            await WriteAsync(buf.AsMemory(0, 5 + data.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>
    /// Читает следующую TLS-запись (заголовок+пейлоад). Возвращает пейлоад в пуловском буфере.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected async ValueTask<(TlsRecordHeader header, byte[] payload)> ReadRecordAsync(CancellationToken cancellationToken)
    {
        var hdr = ArrayPool<byte>.Shared.Rent(5);

        try
        {
            await ReadExactAsync(hdr, cancellationToken).ConfigureAwait(false);

            var header = TlsRecordHeader.Read(hdr);

            var buf = ArrayPool<byte>.Shared.Rent(header.Length);
            await ReadExactAsync(buf.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);

            return (header, buf);
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
            var got = await ReadAsync(mem, cancellationToken).ConfigureAwait(false);
            if (got <= 0) throw new InvalidOperationException("Разрыв соединения при чтении TLS");
            mem = mem[got..];
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer) => stream.Read(buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => stream.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);

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
    public ValueTask HandshakeAsync() => HandshakeAsync(CancellationToken.None);

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
}