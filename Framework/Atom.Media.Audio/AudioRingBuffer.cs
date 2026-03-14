using System.Numerics;
using System.Runtime.CompilerServices;

namespace Atom.Media.Audio;

/// <summary>
/// Lock-free SPSC (Single Producer Single Consumer) кольцевой буфер
/// для потоковой передачи аудио данных между потоками.
/// </summary>
/// <remarks>
/// Гарантирует потокобезопасность при условии, что один поток пишет (Write),
/// а другой читает (Read/Peek/Skip). Для нескольких писателей или читателей
/// требуется внешняя синхронизация.
/// </remarks>
public sealed class AudioRingBuffer
{
    private readonly byte[] buffer;
    private readonly int mask;
    private long head;
    private long tail;

    /// <summary>
    /// Создаёт кольцевой буфер заданной ёмкости.
    /// Фактическая ёмкость округляется вверх до степени двойки.
    /// </summary>
    /// <param name="capacity">Минимальная ёмкость в байтах.</param>
    public AudioRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 1 << 30);

        Capacity = (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
        buffer = GC.AllocateArray<byte>(Capacity, pinned: true);
        mask = Capacity - 1;
    }

    /// <summary>
    /// Фактическая ёмкость буфера в байтах (степень двойки).
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Количество байт, доступных для чтения.
    /// </summary>
    public int AvailableRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)(Volatile.Read(ref head) - Volatile.Read(ref tail));
    }

    /// <summary>
    /// Количество байт, доступных для записи.
    /// </summary>
    public int AvailableWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Capacity - AvailableRead;
    }

    /// <summary>
    /// Записывает данные в буфер.
    /// Если места недостаточно, записывает столько, сколько возможно.
    /// </summary>
    /// <param name="data">Данные для записи.</param>
    /// <returns>Количество фактически записанных байт.</returns>
    public int Write(ReadOnlySpan<byte> data)
    {
        var toWrite = Math.Min(data.Length, AvailableWrite);
        if (toWrite == 0) return 0;

        var h = Volatile.Read(ref head);
        WriteToBuffer(data[..toWrite], (int)(h & mask));
        Volatile.Write(ref head, h + toWrite);
        return toWrite;
    }

    /// <summary>
    /// Читает данные из буфера, продвигая позицию чтения.
    /// </summary>
    /// <param name="data">Буфер для записи прочитанных данных.</param>
    /// <returns>Количество фактически прочитанных байт.</returns>
    public int Read(Span<byte> data)
    {
        var toRead = Math.Min(data.Length, AvailableRead);
        if (toRead == 0) return 0;

        var t = Volatile.Read(ref tail);
        ReadFromBuffer(data[..toRead], (int)(t & mask));
        Volatile.Write(ref tail, t + toRead);
        return toRead;
    }

    /// <summary>
    /// Читает данные из буфера без продвижения позиции чтения.
    /// </summary>
    /// <param name="data">Буфер для записи прочитанных данных.</param>
    /// <returns>Количество фактически прочитанных байт.</returns>
    public int Peek(Span<byte> data)
    {
        var toRead = Math.Min(data.Length, AvailableRead);
        if (toRead == 0) return 0;

        ReadFromBuffer(data[..toRead], (int)(Volatile.Read(ref tail) & mask));
        return toRead;
    }

    /// <summary>
    /// Пропускает указанное количество байт в буфере чтения.
    /// </summary>
    /// <param name="count">Количество байт для пропуска.</param>
    /// <returns>Количество фактически пропущенных байт.</returns>
    public int Skip(int count)
    {
        var toSkip = Math.Min(count, AvailableRead);
        if (toSkip <= 0) return 0;

        Volatile.Write(ref tail, Volatile.Read(ref tail) + toSkip);
        return toSkip;
    }

    /// <summary>
    /// Очищает буфер, сбрасывая позицию чтения к позиции записи.
    /// </summary>
    public void Clear() =>
        Volatile.Write(ref tail, Volatile.Read(ref head));

    private void WriteToBuffer(ReadOnlySpan<byte> data, int offset)
    {
        var firstPart = Math.Min(data.Length, Capacity - offset);
        data[..firstPart].CopyTo(buffer.AsSpan(offset));

        if (firstPart < data.Length)
        {
            data[firstPart..].CopyTo(buffer);
        }
    }

    private void ReadFromBuffer(Span<byte> data, int offset)
    {
        var firstPart = Math.Min(data.Length, Capacity - offset);
        buffer.AsSpan(offset, firstPart).CopyTo(data);

        if (firstPart < data.Length)
        {
            buffer.AsSpan(0, data.Length - firstPart).CopyTo(data[firstPart..]);
        }
    }
}
