#pragma warning disable CA1000, CA2208

using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// Высокооптимизированный декодер Хаффмана.
/// </summary>
/// <remarks>
/// Оптимизации:
/// - Pointer-based доступ без bounds checking
/// - Single lookup для коротких кодов
/// - Branchless декодирование где возможно
/// - Поддержка LSB-first и MSB-first bitstreams
/// </remarks>
public static class HuffmanDecoder
{
    #region Single Symbol Decoding

    /// <summary>
    /// Декодирует один символ из BitReader.
    /// </summary>
    /// <param name="reader">BitReader в режиме LSB-first.</param>
    /// <param name="table">Таблица декодирования.</param>
    /// <returns>Декодированный символ.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Decode(ref BitReader reader, in HuffmanTable table)
    {
        reader.EnsureBits(table.TableLog);
        var bits = reader.PeekBits(table.TableLog);
        var symbol = table.DecodeSymbol(bits, out var consumedBits);
        reader.SkipBits(consumedBits);
        return symbol;
    }

    /// <summary>
    /// Пытается декодировать один символ.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDecode(ref BitReader reader, in HuffmanTable table, out byte symbol)
    {
        if (reader.AvailableBits < table.TableLog)
        {
            symbol = 0;
            return false;
        }

        var bits = reader.PeekBits(table.TableLog);
        symbol = table.DecodeSymbol(bits, out var consumedBits);
        reader.SkipBits(consumedBits);
        return true;
    }

    #endregion

    #region Batch Decoding

    /// <summary>
    /// Пакетное декодирование символов в выходной буфер.
    /// </summary>
    /// <param name="reader">BitReader.</param>
    /// <param name="table">Таблица декодирования.</param>
    /// <param name="output">Выходной буфер.</param>
    /// <param name="count">Количество символов для декодирования.</param>
    /// <returns>Количество декодированных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int DecodeBatch(
        ref BitReader reader,
        in HuffmanTable table,
        Span<byte> output,
        int count)
    {
        var decoded = 0;
        var limit = Math.Min(count, output.Length);
        var tableLog = table.TableLog;

        fixed (byte* outPtr = output)
        {
            while (decoded < limit)
            {
                reader.EnsureBits(tableLog);
                if (reader.AvailableBits < tableLog)
                    break;

                var bits = reader.PeekBits(tableLog);
                outPtr[decoded++] = table.DecodeSymbol(bits, out var consumedBits);
                reader.SkipBits(consumedBits);
            }
        }

        return decoded;
    }

    /// <summary>
    /// Пакетное декодирование с развёрнутым циклом (4 символа за итерацию).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int DecodeBatchUnrolled(
        ref BitReader reader,
        in HuffmanTable table,
        Span<byte> output,
        int count)
    {
        var decoded = 0;
        var limit = Math.Min(count, output.Length);
        var tableLog = table.TableLog;

        fixed (byte* outPtr = output)
        {
            // Развёрнутый цикл: 4 символа
            while (decoded + 4 <= limit)
            {
                reader.EnsureBits(tableLog * 4);
                if (reader.AvailableBits < tableLog * 4)
                    break;

                // Декодируем 4 символа
                var bits0 = reader.PeekBits(tableLog);
                var sym0 = table.DecodeSymbol(bits0, out var len0);
                reader.SkipBits(len0);

                var bits1 = reader.PeekBits(tableLog);
                var sym1 = table.DecodeSymbol(bits1, out var len1);
                reader.SkipBits(len1);

                var bits2 = reader.PeekBits(tableLog);
                var sym2 = table.DecodeSymbol(bits2, out var len2);
                reader.SkipBits(len2);

                var bits3 = reader.PeekBits(tableLog);
                var sym3 = table.DecodeSymbol(bits3, out var len3);
                reader.SkipBits(len3);

                outPtr[decoded] = sym0;
                outPtr[decoded + 1] = sym1;
                outPtr[decoded + 2] = sym2;
                outPtr[decoded + 3] = sym3;
                decoded += 4;
            }

            // Остаток
            while (decoded < limit)
            {
                reader.EnsureBits(tableLog);
                if (reader.AvailableBits < tableLog)
                    break;

                var bits = reader.PeekBits(tableLog);
                outPtr[decoded++] = table.DecodeSymbol(bits, out var len);
                reader.SkipBits(len);
            }
        }

        return decoded;
    }

    #endregion

    #region Reverse Bitstream Decoding (Zstd style)

    /// <summary>
    /// Декодирует reverse LE bitstream (как в Zstd).
    /// </summary>
    /// <param name="stream">Входной поток (читается с конца).</param>
    /// <param name="output">Выходной буфер.</param>
    /// <param name="table">Таблица декодирования.</param>
    /// <returns>Количество декодированных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int DecodeReverseStream(
        ReadOnlySpan<byte> stream,
        Span<byte> output,
        in HuffmanTable table)
    {
        if (stream.IsEmpty) return 0;

        // Encoder записал символы в обратном порядке, поэтому декодер
        // читает из container с LSB, но записывает в output с конца
        var outPos = output.Length - 1;
        var index = stream.Length - 1;
        uint bitContainer = 0;
        var bitCount = 0;
        var mask = table.Mask;
        var tableLog = table.TableLog;

        // Начальное заполнение
        FillBitContainer(ref index, stream, ref bitContainer, ref bitCount);

        // Пропускаем padding (находим старший '1' бит)
        if (!SkipPadding(ref index, stream, ref bitContainer, ref bitCount))
            return 0; // Ошибка: padding не найден

        // Основной цикл декодирования
        while (TryEnsureBits(tableLog, ref index, stream, ref bitContainer, ref bitCount))
        {
            if (outPos < 0)
                break;

            var bits = bitContainer & (uint)mask;
            var symbol = table.DecodeSymbol(bits, out var consumedBits);
            bitContainer >>= consumedBits;
            bitCount -= consumedBits;
            output[outPos--] = symbol;
        }

        // Возвращаем количество декодированных символов
        return output.Length - 1 - outPos;
    }

    /// <summary>
    /// Декодирует reverse stream с ожидаемым количеством символов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DecodeReverseStreamExact(
        ReadOnlySpan<byte> stream,
        Span<byte> output,
        in HuffmanTable table)
    {
        var decoded = DecodeReverseStream(stream, output, table);
        if (decoded != output.Length)
            ThrowDecodingError(decoded, output.Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDecodingError(int decoded, int expected) =>
        throw new InvalidDataException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Huffman decoding mismatch: decoded {decoded}, expected {expected}"));

    #endregion

    #region Bit Container Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillBitContainer(
        ref int index,
        ReadOnlySpan<byte> source,
        ref uint container,
        ref int bitCount)
    {
        // Добавляем один байт за раз (как в Zstd)
        if (index < 0) return;
        container |= (uint)source[index] << bitCount;
        bitCount += 8;
        index--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SkipPadding(
        ref int index,
        ReadOnlySpan<byte> source,
        ref uint container,
        ref int bitCount)
    {
        // В reverse LE bitstream padding записан ПОСЛЕ данных (в старших битах последнего байта).
        // При загрузке последнего байта, padding находится в старших битах container.
        // Padding marker — это старший установленный бит.
        //
        // Формат: [padding_zeros][padding_1][data...]
        // Нам нужно найти самый старший '1' бит и отбросить его вместе с нулями выше.
        if (bitCount == 0)
        {
            if (index < 0) return false;
            FillBitContainer(ref index, source, ref container, ref bitCount);
        }

        if (container == 0) return false;

        // Находим позицию старшего установленного бита
        var highestBit = 31 - System.Numerics.BitOperations.LeadingZeroCount(container);

        // Если highestBit >= bitCount, что-то не так (биты вне диапазона)
        if (highestBit >= bitCount) return false;

        // Отбрасываем padding marker и всё выше него
        // Оставляем биты 0..(highestBit-1) — это данные
        if (highestBit == 0)
        {
            // Только padding marker, нет данных в этом контейнере
            container = 0;
            bitCount = 0;
        }
        else
        {
            var mask = (1u << highestBit) - 1;
            container &= mask;
            bitCount = highestBit;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEnsureBits(
        int requiredBits,
        ref int index,
        ReadOnlySpan<byte> source,
        ref uint container,
        ref int bitCount)
    {
        while (bitCount < requiredBits)
        {
            if (index < 0) return bitCount >= requiredBits;
            container |= (uint)source[index--] << bitCount;
            bitCount += 8;
        }

        return true;
    }

    #endregion

    #region Multi-Stream Decoding (Zstd 4-stream)

    /// <summary>
    /// Декодирует 4 interleaved потока (как в Zstd compressed literals).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Decode4Streams(
        ReadOnlySpan<byte> data,
        Span<byte> output,
        in HuffmanTable table,
        int totalSize,
        int stream1Size,
        int stream2Size,
        int stream3Size)
    {
        // Размеры выходных сегментов
        var segmentSize = (totalSize + 3) / 4;
        var seg1 = Math.Min(segmentSize, totalSize);
        var seg2 = Math.Min(segmentSize, totalSize - seg1);
        var seg3 = Math.Min(segmentSize, totalSize - seg1 - seg2);
        var seg4 = totalSize - seg1 - seg2 - seg3;

        // Декодируем каждый поток в свой сегмент
        var offset = 0;
        var decoded = 0;

        // Stream 1
        var stream1 = data[..stream1Size];
        decoded += DecodeReverseStream(stream1, output[..seg1], table);
        offset += stream1Size;

        // Stream 2
        var stream2 = data.Slice(offset, stream2Size);
        decoded += DecodeReverseStream(stream2, output.Slice(seg1, seg2), table);
        offset += stream2Size;

        // Stream 3
        var stream3 = data.Slice(offset, stream3Size);
        decoded += DecodeReverseStream(stream3, output.Slice(seg1 + seg2, seg3), table);
        offset += stream3Size;

        // Stream 4
        var stream4 = data[offset..];
        decoded += DecodeReverseStream(stream4, output.Slice(seg1 + seg2 + seg3, seg4), table);

        return decoded;
    }

    #endregion
}
