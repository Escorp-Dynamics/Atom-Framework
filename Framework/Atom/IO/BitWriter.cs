#pragma warning disable CA1000, CA1815, S109, S4136, S4275, MA0071, S1066, MA0015, S3928, IDE0032, IDE0022

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO;

/// <summary>
/// Высокопроизводительный писатель битового потока.
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
/// Поддерживает два режима записи:
/// <list type="bullet">
///   <item><b>MSB-first</b> (по умолчанию): биты записываются от старшего к младшему (PNG, JPEG, H.264)</item>
///   <item><b>LSB-first</b>: биты записываются от младшего к старшему (VP8L, DEFLATE)</item>
/// </list>
/// </para>
/// <para>
/// Оптимизации:
/// <list type="bullet">
///   <item>64-bit буфер для минимизации обращений к памяти</item>
///   <item>Zero-allocation (ref struct)</item>
///   <item>Aggressive inlining для hot path</item>
/// </list>
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public ref struct BitWriter
{
    #region Fields

    private readonly Span<byte> data;
    private int bytePosition;
    private ulong buffer;
    private int bufferBits;
    private bool isOverflow;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт BitWriter в MSB-first режиме.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitWriter(Span<byte> destination) : this(destination, lsbFirst: false)
    {
    }

    /// <summary>
    /// Создаёт BitWriter с указанным режимом.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="lsbFirst">true для LSB-first (VP8L, DEFLATE), false для MSB-first (PNG, JPEG, H.264).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitWriter(Span<byte> destination, bool lsbFirst)
    {
        data = destination;
        IsLsbFirst = lsbFirst;
        bytePosition = 0;
        buffer = 0;
        bufferBits = 0;
    }

    #endregion

    #region Properties

    /// <summary>Текущая позиция в битах.</summary>
    public readonly int BitPosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (bytePosition * 8) + bufferBits;
    }

    /// <summary>Количество записанных байт (округлённое вверх).</summary>
    public readonly int BytesWritten
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bytePosition + ((bufferBits + 7) >> 3);
    }

    /// <summary>Оставшееся место в битах.</summary>
    public readonly int RemainingBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (data.Length * 8) - BitPosition;
    }

    /// <summary>Режим LSB-first.</summary>
    public readonly bool IsLsbFirst { get; }

    /// <summary>Общий размер буфера в байтах.</summary>
    public readonly int Capacity => data.Length;

    /// <summary>Общий размер буфера в битах.</summary>
    public readonly int CapacityInBits => data.Length * 8;

    /// <summary>
    /// Указывает, произошло ли переполнение буфера.
    /// </summary>
    /// <remarks>
    /// Используется в сценариях, где предпочтительнее проверка статуса вместо исключений.
    /// После переполнения все последующие операции записи игнорируются.
    /// </remarks>
    public readonly bool IsOverflow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => isOverflow;
    }

    #endregion

    #region Write Methods

    /// <summary>
    /// Записывает один бит.
    /// </summary>
    /// <param name="bit">true для бита 1, false для бита 0.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBit(bool bit)
    {
        if (isOverflow)
            return;

        if (IsLsbFirst)
        {
            if (bit)
                buffer |= 1UL << bufferBits;
        }
        else
        {
            if (bit)
                buffer |= 0x8000_0000_0000_0000UL >> bufferBits;
        }

        bufferBits++;

        if (bufferBits >= 8)
            TryFlushOneByte();
    }

    /// <summary>
    /// Пробует записать один бит.
    /// </summary>
    /// <param name="bit">true для бита 1, false для бита 0.</param>
    /// <returns>true если успешно, false если буфер переполнен.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteBit(bool bit)
    {
        WriteBit(bit);
        return !isOverflow;
    }

    /// <summary>
    /// Записывает указанное количество бит (0-32).
    /// </summary>
    /// <param name="value">Значение для записи.</param>
    /// <param name="count">Количество бит для записи (0-32).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBits(uint value, int count)
    {
        if (isOverflow)
            return;

        if ((uint)count > 32)
            ThrowInvalidBitCount();

        if (count == 0)
            return;

        if (IsLsbFirst)
        {
            value &= (uint)((1UL << count) - 1);
            buffer |= (ulong)value << bufferBits;
        }
        else
        {
            value &= (uint)((1UL << count) - 1);
            buffer |= (ulong)value << (64 - bufferBits - count);
        }

        bufferBits += count;

        while (bufferBits >= 8 && !isOverflow)
            TryFlushOneByte();
    }

    /// <summary>
    /// Пробует записать указанное количество бит (0-32).
    /// </summary>
    /// <param name="value">Значение для записи.</param>
    /// <param name="count">Количество бит для записи (0-32).</param>
    /// <returns>true если успешно, false если буфер переполнен.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteBits(uint value, int count)
    {
        WriteBits(value, count);
        return !isOverflow;
    }

    /// <summary>
    /// Записывает знаковое значение.
    /// </summary>
    /// <param name="value">Знаковое значение.</param>
    /// <param name="count">Количество бит (1-32).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSignedBits(int value, int count)
    {
        var mask = (1U << count) - 1;
        WriteBits((uint)value & mask, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryFlushOneByte()
    {
        if ((uint)bytePosition >= (uint)data.Length)
        {
            isOverflow = true;
            return;
        }

        if (IsLsbFirst)
        {
            data[bytePosition++] = (byte)(buffer & 0xFF);
            buffer >>= 8;
        }
        else
        {
            data[bytePosition++] = (byte)(buffer >> 56);
            buffer <<= 8;
        }

        bufferBits -= 8;
    }

    #endregion

    #region Write Bytes Methods

    /// <summary>
    /// Записывает один байт (8 бит).
    /// </summary>
    /// <param name="value">Байт для записи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value) => WriteBits(value, 8);

    /// <summary>
    /// Записывает 16-bit unsigned integer.
    /// </summary>
    /// <param name="value">16-bit значение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        if (IsLsbFirst)
        {
            WriteBits((byte)(value & 0xFF), 8);
            WriteBits((byte)(value >> 8), 8);
        }
        else
        {
            WriteBits(value, 16);
        }
    }

    /// <summary>
    /// Записывает 32-bit unsigned integer.
    /// </summary>
    /// <param name="value">32-bit значение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        if (IsLsbFirst)
        {
            WriteBits((byte)(value & 0xFF), 8);
            WriteBits((byte)((value >> 8) & 0xFF), 8);
            WriteBits((byte)((value >> 16) & 0xFF), 8);
            WriteBits((byte)(value >> 24), 8);
        }
        else
        {
            WriteBits(value, 32);
        }
    }

    /// <summary>
    /// Записывает массив байт.
    /// </summary>
    /// <param name="bytes">Байты для записи.</param>
    /// <remarks>
    /// Автоматически выравнивает позицию на границу байта перед записью.
    /// </remarks>
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (isOverflow)
            return;

        AlignToByte();

        if (bytePosition + bytes.Length > data.Length)
        {
            isOverflow = true;
            return;
        }

        bytes.CopyTo(data[bytePosition..]);
        bytePosition += bytes.Length;
    }

    /// <summary>
    /// Пробует записать массив байт.
    /// </summary>
    /// <param name="bytes">Байты для записи.</param>
    /// <returns>true если успешно, false если буфер переполнен.</returns>
    public bool TryWriteBytes(ReadOnlySpan<byte> bytes)
    {
        WriteBytes(bytes);
        return !isOverflow;
    }

    #endregion

    #region Align Methods

    /// <summary>
    /// Выравнивает позицию на границу байта.
    /// </summary>
    /// <remarks>
    /// Дополняет оставшиеся биты нулями до следующей границы байта.
    /// Если позиция уже выровнена — ничего не делает.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AlignToByte()
    {
        if ((bufferBits & 7) != 0)
        {
            var padding = 8 - (bufferBits & 7);
            WriteBits(0, padding);
        }
    }

    /// <summary>
    /// Выравнивает на границу байта, заполняя указанным значением.
    /// </summary>
    /// <param name="fillValue">Значение для заполнения (0 или 1).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AlignToByte(bool fillValue)
    {
        if ((bufferBits & 7) != 0)
        {
            var padding = 8 - (bufferBits & 7);
            var value = fillValue ? (1U << padding) - 1 : 0U;
            WriteBits(value, padding);
        }
    }

    #endregion

    #region Flush Methods

    /// <summary>
    /// Сбрасывает все накопленные биты в буфер.
    /// </summary>
    /// <remarks>
    /// Вызывать после завершения записи для сброса последнего неполного байта.
    /// Неполный байт дополняется нулями.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        if (bufferBits > 0)
        {
            AlignToByte();
        }
    }

    /// <summary>
    /// Финализирует поток с padding по RFC 8878 (Zstd/FSE).
    /// </summary>
    /// <remarks>
    /// <para>
    /// По спецификации RFC 8878 битовый поток FSE завершается:
    /// <list type="number">
    ///   <item>Записывает завершающий бит '1'</item>
    ///   <item>Доводит текущий байт до границы нулями</item>
    ///   <item>Сбрасывает последний байт</item>
    /// </list>
    /// </para>
    /// <para>
    /// Результирующий последний байт не может быть 0 (всегда содержит минимум бит '1').
    /// </para>
    /// </remarks>
    /// <returns>true если успешно, false если буфер переполнен.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryFinishWithPadding()
    {
        if (isOverflow)
            return false;

        // 1) Записываем завершающий бит '1'
        if (!TryWriteBits(1, 1))
            return false;

        // 2) Доводим до границы байта нулями
        if ((bufferBits & 7) != 0)
        {
            var pad = 8 - (bufferBits & 7);
            if (!TryWriteBits(0, pad))
                return false;
        }

        // 3) Сбрасываем последний байт (если есть)
        if (bufferBits > 0)
        {
            if ((uint)bytePosition >= (uint)data.Length)
            {
                isOverflow = true;
                return false;
            }

            data[bytePosition++] = (byte)(buffer & 0xFF);
            buffer = 0;
            bufferBits = 0;
        }

        return true;
    }

    /// <summary>
    /// Финализирует поток с padding, бросает исключение при ошибке.
    /// </summary>
    /// <exception cref="InvalidOperationException">Буфер переполнен.</exception>
    public void FinishWithPadding()
    {
        if (!TryFinishWithPadding())
            ThrowBufferFull();
    }

    /// <summary>
    /// Сбрасывает буфер и возвращает записанные данные.
    /// </summary>
    /// <returns>Span записанных данных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetWrittenSpan()
    {
        Flush();
        return data[..bytePosition];
    }

    /// <summary>
    /// Сбрасывает буфер и возвращает записанные данные как ReadOnlySpan.
    /// </summary>
    /// <returns>ReadOnlySpan записанных данных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetWrittenReadOnlySpan()
    {
        Flush();
        return data[..bytePosition];
    }

    #endregion

    #region Reset

    /// <summary>
    /// Сбрасывает писатель в начальное состояние.
    /// </summary>
    public void Reset()
    {
        bytePosition = 0;
        buffer = 0;
        bufferBits = 0;
        isOverflow = false;
    }

    /// <summary>
    /// Очищает флаг переполнения без сброса позиции.
    /// </summary>
    /// <remarks>
    /// Используется для повторной попытки записи после расширения буфера.
    /// </remarks>
    public void ClearOverflow()
    {
        isOverflow = false;
    }

    #endregion

    #region Exception Helpers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidBitCount() =>
        throw new ArgumentOutOfRangeException(nameof(WriteBits), "Количество бит должно быть от 0 до 32");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBufferFull() =>
        throw new InvalidOperationException("Буфер записи переполнен");

    #endregion
}
