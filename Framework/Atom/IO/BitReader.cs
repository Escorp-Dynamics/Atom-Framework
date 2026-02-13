#pragma warning disable CA1000, CA1815, S109, S4136, S4275, MA0071, S1066, MA0015, S3928

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO;

/// <summary>
/// Высокопроизводительный читатель битового потока.
/// </summary>
/// <remarks>
/// <para>
/// Универсальный примитив для работы с битовыми потоками в:
/// <list type="bullet">
///   <item><b>Сжатие:</b> DEFLATE, Brotli, Zstd, LZ4</item>
///   <item><b>Кодеки:</b> PNG, JPEG, WebP (VP8L), H.264, VP9</item>
///   <item><b>Криптография:</b> ASN.1 DER, сертификаты</item>
///   <item><b>Протоколы:</b> QUIC, HTTP/2 HPACK, gRPC</item>
/// </list>
/// </para>
/// <para>
/// Поддерживает два режима чтения:
/// <list type="bullet">
///   <item><b>MSB-first</b> (по умолчанию): биты читаются от старшего к младшему (PNG, JPEG, H.264)</item>
///   <item><b>LSB-first</b>: биты читаются от младшего к старшему (VP8L, DEFLATE)</item>
/// </list>
/// </para>
/// <para>
/// Оптимизации:
/// <list type="bullet">
///   <item>64-bit буфер для минимизации обращений к памяти</item>
///   <item>Branchless операции где возможно</item>
///   <item>Zero-allocation (ref struct)</item>
///   <item>Aggressive inlining для hot path</item>
/// </list>
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public ref struct BitReader
{
    #region Fields

    private readonly ReadOnlySpan<byte> data;
    private ulong buffer;
    private int bufferBits;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт BitReader в MSB-first режиме.
    /// </summary>
    /// <param name="data">Данные для чтения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitReader(ReadOnlySpan<byte> data) : this(data, lsbFirst: false)
    {
    }

    /// <summary>
    /// Создаёт BitReader с указанным режимом.
    /// </summary>
    /// <param name="data">Данные для чтения.</param>
    /// <param name="lsbFirst">true для LSB-first (VP8L, DEFLATE), false для MSB-first (PNG, JPEG, H.264).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitReader(ReadOnlySpan<byte> data, bool lsbFirst)
    {
        this.data = data;
        IsLsbFirst = lsbFirst;
        BytesConsumed = 0;
        buffer = 0;
        bufferBits = 0;
        FillBuffer();
    }

    #endregion

    #region Properties

    /// <summary>Текущая позиция в битах от начала данных.</summary>
    public readonly int BitPosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (BytesConsumed * 8) - bufferBits;
    }

    /// <summary>Текущая позиция в байтах (округлённая вниз).</summary>
    public readonly int BytePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitPosition >> 3;
    }

    /// <summary>Количество оставшихся бит.</summary>
    public readonly int RemainingBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (data.Length * 8) - BitPosition;
    }

    /// <summary>Достигнут ли конец данных.</summary>
    public readonly bool IsAtEnd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bufferBits == 0 && BytesConsumed >= data.Length;
    }

    /// <summary>Режим LSB-first.</summary>
    public readonly bool IsLsbFirst { get; }

    /// <summary>Количество байт, фактически прочитанных из источника.</summary>
    /// <remarks>
    /// Это внутренняя позиция чтения, используемая для заполнения буфера.
    /// Может быть больше <see cref="BytePosition"/> из-за предзагрузки в буфер.
    /// </remarks>
    public int BytesConsumed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get; private set;
    }

    /// <summary>Количество доступных бит (в буфере + ещё не прочитанные байты).</summary>
    public readonly int AvailableBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bufferBits + ((data.Length - BytesConsumed) * 8);
    }

    /// <summary>Общая длина данных в байтах.</summary>
    public readonly int Length => data.Length;

    /// <summary>Общая длина данных в битах.</summary>
    public readonly int LengthInBits => data.Length * 8;

    #endregion

    #region Read Methods

    /// <summary>
    /// Читает один бит.
    /// </summary>
    /// <returns>true если бит = 1, false если бит = 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBit()
    {
        if (bufferBits == 0)
            FillBuffer();

        if (bufferBits == 0)
            ThrowEndOfData();

        bool result;

        if (IsLsbFirst)
        {
            result = (buffer & 1) != 0;
            buffer >>= 1;
        }
        else
        {
            result = (buffer & 0x8000_0000_0000_0000UL) != 0;
            buffer <<= 1;
        }

        bufferBits--;
        return result;
    }

    /// <summary>
    /// Читает указанное количество бит (0-32).
    /// </summary>
    /// <param name="count">Количество бит для чтения (0-32).</param>
    /// <returns>Прочитанное значение.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBits(int count)
    {
        if ((uint)count > 32)
            ThrowInvalidBitCount();

        if (count == 0)
            return 0;

        while (bufferBits < count && TryFillBuffer())
        {
            // Продолжаем заполнять буфер
        }

        if (bufferBits < count)
        {
            if (bufferBits == 0)
                ThrowEndOfData();
            count = bufferBits;
        }

        uint result;

        if (IsLsbFirst)
        {
            result = (uint)(buffer & ((1UL << count) - 1));
            buffer >>= count;
        }
        else
        {
            result = (uint)(buffer >> (64 - count));
            buffer <<= count;
        }

        bufferBits -= count;
        return result;
    }

    /// <summary>
    /// Читает указанное количество бит как знаковое число.
    /// </summary>
    /// <param name="count">Количество бит для чтения (1-32).</param>
    /// <returns>Знаковое значение с расширением знака.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadSignedBits(int count)
    {
        var value = ReadBits(count);
        var signBit = 1U << (count - 1);
        if ((value & signBit) != 0)
            value |= ~((1U << count) - 1);
        return (int)value;
    }

    /// <summary>
    /// Просматривает биты без продвижения позиции.
    /// </summary>
    /// <param name="count">Количество бит для просмотра (0-32).</param>
    /// <returns>Значение бит без изменения позиции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint PeekBits(int count)
    {
        if ((uint)count > 32 || count > bufferBits)
            return PeekBitsSlow(count);

        if (IsLsbFirst)
            return (uint)(buffer & ((1UL << count) - 1));

        return (uint)(buffer >> (64 - count));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly uint PeekBitsSlow(int count)
    {
        if ((uint)count > 32)
            ThrowInvalidBitCount();

        if (count == 0)
            return 0;

        var tempReader = this;
        return tempReader.ReadBits(count);
    }

    /// <summary>
    /// Пробует прочитать указанное количество бит.
    /// </summary>
    /// <param name="count">Количество бит для чтения.</param>
    /// <param name="value">Прочитанное значение.</param>
    /// <returns>true если удалось прочитать, false если недостаточно данных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBits(int count, out uint value)
    {
        if (RemainingBits < count)
        {
            value = 0;
            return false;
        }

        value = ReadBits(count);
        return true;
    }

    #endregion

    #region Skip & Align Methods

    /// <summary>
    /// Пропускает указанное количество бит.
    /// </summary>
    /// <param name="count">Количество бит для пропуска.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipBits(int count)
    {
        while (count > 0)
        {
            if (bufferBits == 0 && !TryFillBuffer())
                ThrowEndOfData();

            var toSkip = Math.Min(count, bufferBits);

            if (IsLsbFirst)
                buffer >>= toSkip;
            else
                buffer <<= toSkip;

            bufferBits -= toSkip;
            count -= toSkip;
        }
    }

    /// <summary>
    /// Выравнивает позицию на границу байта.
    /// </summary>
    /// <remarks>
    /// Пропускает оставшиеся биты до следующей границы байта.
    /// Если позиция уже выровнена — ничего не делает.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AlignToByte()
    {
        var bitsInCurrentByte = bufferBits & 7;
        if (bitsInCurrentByte != 0)
            SkipBits(bitsInCurrentByte);
    }

    #endregion

    #region Read Bytes Methods

    /// <summary>
    /// Читает один байт (8 бит).
    /// </summary>
    /// <returns>Прочитанный байт.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte() => (byte)ReadBits(8);

    /// <summary>
    /// Читает 16-bit unsigned integer.
    /// </summary>
    /// <returns>16-bit значение (big-endian в MSB, little-endian в LSB режиме).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16()
    {
        if (IsLsbFirst)
        {
            var lo = ReadBits(8);
            var hi = ReadBits(8);
            return (ushort)((hi << 8) | lo);
        }
        return (ushort)ReadBits(16);
    }

    /// <summary>
    /// Читает 32-bit unsigned integer.
    /// </summary>
    /// <returns>32-bit значение.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32()
    {
        if (IsLsbFirst)
        {
            var b0 = ReadBits(8);
            var b1 = ReadBits(8);
            var b2 = ReadBits(8);
            var b3 = ReadBits(8);
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }
        return ReadBits(32);
    }

    /// <summary>
    /// Читает байты в буфер.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <returns>Количество прочитанных байт.</returns>
    /// <remarks>
    /// Автоматически выравнивает позицию на границу байта перед чтением.
    /// </remarks>
    public int ReadBytes(Span<byte> destination)
    {
        AlignToByte();

        var available = RemainingBits >> 3;
        var toRead = Math.Min(available, destination.Length);

        var bufferBytes = bufferBits >> 3;
        var fromBuffer = Math.Min(bufferBytes, toRead);

        for (var i = 0; i < fromBuffer; i++)
            destination[i] = (byte)ReadBits(8);

        var remaining = toRead - fromBuffer;
        if (remaining > 0)
        {
            var currentPos = BytesConsumed;
            data.Slice(currentPos, remaining).CopyTo(destination[fromBuffer..]);
            BytesConsumed += remaining;
        }

        return toRead;
    }

    #endregion

    #region Buffer Management

    /// <summary>
    /// Гарантирует наличие минимум указанного количества бит в буфере.
    /// </summary>
    /// <param name="count">Минимальное количество бит, которое должно быть в буфере.</param>
    /// <remarks>
    /// Используется для оптимизации чтения: позволяет загрузить данные заранее,
    /// чтобы последующие операции ReadBits были быстрее.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureBits(int count)
    {
        while (bufferBits < count && BytesConsumed < data.Length)
        {
            var b = data[BytesConsumed++];

            if (IsLsbFirst)
                buffer |= (ulong)b << bufferBits;
            else
                buffer |= (ulong)b << (56 - bufferBits);

            bufferBits += 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillBuffer() => TryFillBuffer();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFillBuffer()
    {
        var filled = false;

        while (bufferBits <= 56 && BytesConsumed < data.Length)
        {
            var b = data[BytesConsumed++];

            if (IsLsbFirst)
                buffer |= (ulong)b << bufferBits;
            else
                buffer |= (ulong)b << (56 - bufferBits);

            bufferBits += 8;
            filled = true;
        }

        return filled;
    }

    #endregion

    #region Seek

    /// <summary>
    /// Устанавливает позицию чтения в битах.
    /// </summary>
    /// <param name="position">Позиция в битах от начала данных.</param>
    public void Seek(int position)
    {
        if (position < 0 || position > data.Length * 8)
            ThrowInvalidPosition();

        BytesConsumed = position >> 3;
        buffer = 0;
        bufferBits = 0;

        FillBuffer();

        var bitsToSkip = position & 7;
        if (bitsToSkip > 0)
            SkipBits(bitsToSkip);
    }

    /// <summary>
    /// Сбрасывает читатель в начальное состояние.
    /// </summary>
    public void Reset()
    {
        BytesConsumed = 0;
        buffer = 0;
        bufferBits = 0;
        FillBuffer();
    }

    #endregion

    #region Exception Helpers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEndOfData() =>
        throw new InvalidOperationException("Неожиданный конец данных в битовом потоке");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidBitCount() =>
        throw new ArgumentOutOfRangeException(nameof(ReadBits), "Количество бит должно быть от 0 до 32");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidPosition() =>
        throw new ArgumentOutOfRangeException(nameof(Seek), "Позиция за пределами данных");

    #endregion
}
