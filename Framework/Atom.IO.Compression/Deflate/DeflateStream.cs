#pragma warning disable CA2215, S4136

using System.IO.Compression;
using System.Runtime.CompilerServices;
using Atom.IO.Compression.Deflate;

namespace Atom.IO.Compression;

/// <summary>
/// Высокопроизводительный потоковый Deflate (RFC 1951).
/// Drop-in замена System.IO.Compression.DeflateStream с улучшенной производительностью.
/// </summary>
/// <remarks>
/// Особенности:
/// - SIMD-оптимизированное LZ77 сопоставление
/// - Переиспользование буферов через workspace pooling
/// - Zero-allocation hot path
/// - Совместимость с PNG, ZIP, HTTP gzip/deflate
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class DeflateStream(System.IO.Stream stream, CompressionMode mode, bool LeaveOpen) : System.IO.Stream
{
    #region Constants

    /// <summary>Максимальный размер окна LZ77 (32KB).</summary>
    internal const int MaxWindowSize = 32768;

    /// <summary>Максимальная длина match (258 байт).</summary>
    internal const int MaxMatchLength = 258;

    /// <summary>Минимальная длина match (3 байта).</summary>
    internal const int MinMatchLength = 3;

    /// <summary>Максимальная дистанция (32768).</summary>
    internal const int MaxDistance = 32768;

    #endregion

    #region Fields

    private readonly CompressionMode mode = mode;

    private DeflateDecoder? decoder;
    private DeflateEncoder? encoder;

    private bool isDisposed;

    #endregion

    #region Properties

    /// <summary>Уровень сжатия (только для режима Compress).</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;

    /// <summary>Базовый поток.</summary>
    public System.IO.Stream BaseStream { get; } = stream;

    /// <summary>Оставить базовый поток открытым при Dispose.</summary>
    private bool LeaveOpen { get; } = LeaveOpen;

    /// <inheritdoc/>
    public override bool CanRead => mode == CompressionMode.Decompress && BaseStream.CanRead;

    /// <inheritdoc/>
    public override bool CanWrite => mode == CompressionMode.Compress && BaseStream.CanWrite;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт DeflateStream с указанным режимом. Базовый поток закрывается при Dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DeflateStream(System.IO.Stream stream, CompressionMode mode)
        : this(stream, mode, LeaveOpen: false) { }

    /// <summary>
    /// Создаёт DeflateStream с указанным уровнем сжатия (режим Compress).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DeflateStream(System.IO.Stream stream, CompressionLevel compressionLevel, bool LeaveOpen = false)
        : this(stream, CompressionMode.Compress, LeaveOpen) => CompressionLevel = compressionLevel;

    #endregion

    #region Read (Decompress)

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (mode != CompressionMode.Decompress)
            throw new InvalidOperationException("Cannot read from a compression stream.");

        decoder ??= new DeflateDecoder(BaseStream);
        return decoder.Read(buffer);
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        Span<byte> buf = stackalloc byte[1];
        return Read(buf) == 1 ? buf[0] : -1;
    }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (mode != CompressionMode.Decompress)
            throw new InvalidOperationException("Cannot read from a compression stream.");

        decoder ??= new DeflateDecoder(BaseStream);
        return new ValueTask<int>(decoder.Read(buffer.Span));
    }

    #endregion

    #region Write (Compress)

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (mode != CompressionMode.Compress)
            throw new InvalidOperationException("Cannot write to a decompression stream.");

        encoder ??= new DeflateEncoder(BaseStream, CompressionLevel);
        encoder.Write(buffer);
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        ReadOnlySpan<byte> buf = [value];
        Write(buf);
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (mode != CompressionMode.Compress)
            throw new InvalidOperationException("Cannot write to a decompression stream.");

        encoder ??= new DeflateEncoder(BaseStream, CompressionLevel);
        return encoder.WriteAsync(buffer, cancellationToken);
    }

    #endregion

    #region Flush

    /// <inheritdoc/>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (mode == CompressionMode.Compress)
        {
            encoder?.Flush();
        }

        BaseStream.Flush();
    }

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (mode == CompressionMode.Compress && encoder != null)
        {
            await encoder.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Not Supported

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) =>
        throw new NotSupportedException();

    #endregion

    #region Dispose

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (isDisposed) return;

        if (disposing)
        {
            try
            {
                // Финализируем сжатие (пишем final block)
                if (mode == CompressionMode.Compress)
                {
                    encoder?.Finish();
                }
            }
            finally
            {
                decoder?.Dispose();
                encoder?.Dispose();

                if (!LeaveOpen)
                {
                    BaseStream.Dispose();
                }
            }
        }

        isDisposed = true;
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (isDisposed) return;

        try
        {
            if (mode == CompressionMode.Compress && encoder != null)
            {
                await encoder.FinishAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            decoder?.Dispose();
            encoder?.Dispose();

            if (!LeaveOpen)
            {
                await BaseStream.DisposeAsync().ConfigureAwait(false);
            }
        }

        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
