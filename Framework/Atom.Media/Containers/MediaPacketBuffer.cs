#pragma warning disable IDE0032

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Буфер для пакета (владеет памятью).
/// </summary>
public sealed class MediaPacketBuffer : IDisposable
{
    private byte[]? rentedBuffer;
    private int dataSize;
    private bool isDisposed;

    /// <summary>
    /// Индекс потока.
    /// </summary>
    public int StreamIndex { get; set; }

    /// <summary>
    /// PTS в микросекундах.
    /// </summary>
    public long PtsUs { get; set; }

    /// <summary>
    /// DTS в микросекундах.
    /// </summary>
    public long DtsUs { get; set; }

    /// <summary>
    /// Duration в микросекундах.
    /// </summary>
    public long DurationUs { get; set; }

    /// <summary>
    /// Свойства пакета.
    /// </summary>
    public PacketProperty Properties { get; set; }

    /// <summary>
    /// Размер данных.
    /// </summary>
    public int Size => dataSize;

    /// <summary>
    /// Возвращает true, если буфер выделен.
    /// </summary>
    public bool IsAllocated => rentedBuffer is not null;

    /// <summary>
    /// Создаёт буфер с начальной ёмкостью.
    /// </summary>
    public MediaPacketBuffer(int initialCapacity = 0)
    {
        if (initialCapacity > 0)
            rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    /// <summary>
    /// Гарантирует минимальную ёмкость буфера.
    /// </summary>
    public void EnsureCapacity(int capacity)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (rentedBuffer is not null && rentedBuffer.Length >= capacity)
            return;

        var newBuffer = ArrayPool<byte>.Shared.Rent(capacity);

        if (rentedBuffer is not null)
        {
            rentedBuffer.AsSpan(0, dataSize).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }

        rentedBuffer = newBuffer;
    }

    /// <summary>
    /// Записывает данные в буфер.
    /// </summary>
    public void SetData(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(rentedBuffer);
        dataSize = data.Length;
    }

    /// <summary>
    /// Возвращает Span для записи данных.
    /// </summary>
    public Span<byte> GetWriteSpan(int size)
    {
        EnsureCapacity(size);
        dataSize = size;
        return rentedBuffer.AsSpan(0, size);
    }

    /// <summary>
    /// Возвращает пакет.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MediaPacket AsPacket()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        return rentedBuffer is not null
            ? new MediaPacket(rentedBuffer.AsSpan(0, dataSize), StreamIndex, PtsUs, DtsUs, DurationUs, Properties)
            : default;
    }

    /// <summary>
    /// Возвращает данные как ReadOnlySpan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetData()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return rentedBuffer is not null ? rentedBuffer.AsSpan(0, dataSize) : default;
    }

    /// <summary>
    /// Возвращает данные как Memory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<byte> GetMemory()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return (rentedBuffer?.AsMemory(0, dataSize)) ?? default;
    }

    /// <summary>
    /// Очищает буфер.
    /// </summary>
    public void Clear()
    {
        dataSize = 0;
        StreamIndex = 0;
        PtsUs = 0;
        DtsUs = 0;
        DurationUs = 0;
        Properties = PacketProperty.None;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (rentedBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
            rentedBuffer = null;
        }
    }
}
