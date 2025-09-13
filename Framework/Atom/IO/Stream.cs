#pragma warning disable CA1816, CA2215

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.IO;

/// <summary>
/// Представляет базовую реализацию оптимизированного под NativeAOT <see cref="System.IO.Stream"/>.
/// </summary>
public abstract class Stream : System.IO.Stream
{
    /// <inheritdoc/>
    public sealed override bool CanSeek
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    /// <inheritdoc/>
    public sealed override long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public sealed override long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => throw new NotSupportedException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public abstract override bool CanRead { get; }

    /// <inheritdoc/>
    public abstract override bool CanWrite { get; }

    /// <inheritdoc/>
    public sealed override bool CanTimeout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    /// <inheritdoc/>
    public sealed override int ReadTimeout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => throw new NotSupportedException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public sealed override int WriteTimeout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => throw new NotSupportedException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract override int Read(Span<byte> buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract override void Write(ReadOnlySpan<byte> buffer);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override int EndRead(IAsyncResult asyncResult) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void EndWrite(IAsyncResult asyncResult) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush() { }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        var n = Read(one);
        return n is 0 ? -1 : one[0];
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void WriteByte(byte value)
    {
        ReadOnlySpan<byte> one = [value];
        Write(one);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void CopyTo(System.IO.Stream destination, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        var rented = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            var span = rented.AsSpan(0, bufferSize);

            while (true)
            {
                var read = Read(span);
                if (read <= 0) break;
                destination.Write(span[..read]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override async Task CopyToAsync([NotNull] System.IO.Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            var mem = rented.AsMemory(0, bufferSize);

            while (true)
            {
                var n = await ReadAsync(mem, cancellationToken).ConfigureAwait(false);
                if (n <= 0) break;
                await destination.WriteAsync(mem[..n], cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void Close()
    {
        try { Dispose(true); }
        finally { GC.SuppressFinalize(this); }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining), Obsolete("Метод является устаревшим")]
    protected override WaitHandle CreateWaitHandle() => throw new NotSupportedException("WaitHandle для данного потока не поддерживается");

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing) { /* Не используется в текущей реализации */ }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask DisposeAsync()
    {
        try
        {
            Dispose(true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
#if NETFRAMEWORK
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override object InitializeLifetimeService() => default!;
#endif
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining), Obsolete("Метод является устаревшим")]
    protected override void ObjectInvariant() { /* Не поддерживается */ }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => GetType().FullName ?? nameof(Stream);
}