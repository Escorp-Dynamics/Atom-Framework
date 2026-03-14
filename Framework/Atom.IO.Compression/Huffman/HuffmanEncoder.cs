#pragma warning disable CA1000, CA2208, S3776, S4136

using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// Высокооптимизированный кодировщик Хаффмана.
/// </summary>
/// <remarks>
/// Оптимизации:
/// - Прямой доступ к таблице кодов без bounds checking
/// - Поддержка LSB-first и MSB-first записи
/// - Пакетное кодирование
/// </remarks>
public static class HuffmanEncoder
{
    #region Single Symbol Encoding

    /// <summary>
    /// Кодирует один символ в BitWriter.
    /// </summary>
    /// <param name="writer">BitWriter.</param>
    /// <param name="symbol">Символ для кодирования.</param>
    /// <param name="codes">Массив кодов (индексируется символом).</param>
    /// <param name="lengths">Массив длин (индексируется символом).</param>
    /// <returns>True если успешно, false если overflow.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncode(
        ref BitWriter writer,
        byte symbol,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var code = codes[symbol];
        var length = lengths[symbol];
        return length != 0 && writer.TryWriteBits(code, length);
    }

    /// <summary>
    /// Кодирует один символ с HuffmanCode структурой.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncode(ref BitWriter writer, in HuffmanCode huffmanCode)
    {
        if (huffmanCode.IsEmpty) return false;
        return writer.TryWriteBits(huffmanCode.Code, huffmanCode.Length);
    }

    /// <summary>
    /// Кодирует один символ (unsafe версия без проверки overflow).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode(
        ref BitWriter writer,
        byte symbol,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var code = codes[symbol];
        var length = lengths[symbol];
        writer.WriteBits(code, length);
    }

    #endregion

    #region Single Symbol Encoding (16-bit)

    /// <summary>
    /// Кодирует один 16-битный символ в BitWriter.
    /// </summary>
    /// <param name="writer">BitWriter.</param>
    /// <param name="symbol">Символ для кодирования (0..65535).</param>
    /// <param name="codes">Массив кодов (индексируется символом).</param>
    /// <param name="lengths">Массив длин (индексируется символом).</param>
    /// <returns>True если успешно, false если overflow.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncode(
        ref BitWriter writer,
        ushort symbol,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var code = codes[symbol];
        var length = lengths[symbol];
        return length != 0 && writer.TryWriteBits(code, length);
    }

    /// <summary>
    /// Кодирует один 16-битный символ (unsafe версия без проверки overflow).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Encode(
        ref BitWriter writer,
        ushort symbol,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var code = codes[symbol];
        var length = lengths[symbol];
        writer.WriteBits(code, length);
    }

    #endregion

    #region Batch Encoding (16-bit)

    /// <summary>
    /// Пакетное кодирование 16-битных символов.
    /// </summary>
    /// <param name="writer">BitWriter.</param>
    /// <param name="symbols">Входные символы.</param>
    /// <param name="codes">Массив кодов.</param>
    /// <param name="lengths">Массив длин.</param>
    /// <returns>Количество успешно закодированных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int EncodeBatch(
        ref BitWriter writer,
        ReadOnlySpan<ushort> symbols,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var encoded = 0;

        fixed (ushort* symPtr = symbols)
        fixed (byte* lenPtr = lengths)
        fixed (uint* codePtr = codes)
        {
            var count = symbols.Length;
            for (var i = 0; i < count; i++)
            {
                var symbol = symPtr[i];
                var code = codePtr[symbol];
                var length = lenPtr[symbol];

                if (length == 0 || !writer.TryWriteBits(code, length))
                    break;

                encoded++;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Пакетное кодирование 16-битных символов с развёрнутым циклом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int EncodeBatchUnrolled(
        ref BitWriter writer,
        ReadOnlySpan<ushort> symbols,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var encoded = 0;
        var count = symbols.Length;

        fixed (ushort* symPtr = symbols)
        fixed (uint* codePtr = codes)
        fixed (byte* lenPtr = lengths)
        {
            // Развёрнутый цикл: 4 символа
            while (encoded + 4 <= count)
            {
                var s0 = symPtr[encoded];
                var s1 = symPtr[encoded + 1];
                var s2 = symPtr[encoded + 2];
                var s3 = symPtr[encoded + 3];

                var l0 = lenPtr[s0];
                var l1 = lenPtr[s1];
                var l2 = lenPtr[s2];
                var l3 = lenPtr[s3];

                // Проверяем, что все символы валидны
                if (l0 == 0 || l1 == 0 || l2 == 0 || l3 == 0)
                    break;

                // Пробуем записать все 4 символа
                if (!writer.TryWriteBits(codePtr[s0], l0)) break;
                if (!writer.TryWriteBits(codePtr[s1], l1)) break;
                if (!writer.TryWriteBits(codePtr[s2], l2)) break;
                if (!writer.TryWriteBits(codePtr[s3], l3)) break;

                encoded += 4;
            }

            // Остаток
            while (encoded < count)
            {
                var s = symPtr[encoded];
                var l = lenPtr[s];
                var c = codePtr[s];

                if (l == 0 || !writer.TryWriteBits(c, l))
                    break;

                encoded++;
            }
        }

        return encoded;
    }

    #endregion

    #region Batch Encoding

    /// <summary>
    /// Пакетное кодирование символов.
    /// </summary>
    /// <param name="writer">BitWriter.</param>
    /// <param name="symbols">Входные символы.</param>
    /// <param name="codes">Массив кодов.</param>
    /// <param name="lengths">Массив длин.</param>
    /// <returns>Количество успешно закодированных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int EncodeBatch(
        ref BitWriter writer,
        ReadOnlySpan<byte> symbols,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var encoded = 0;

        fixed (byte* symPtr = symbols, lenPtr = lengths)
        fixed (uint* codePtr = codes)
        {
            var count = symbols.Length;
            for (var i = 0; i < count; i++)
            {
                var symbol = symPtr[i];
                var code = codePtr[symbol];
                var length = lenPtr[symbol];

                if (length == 0 || !writer.TryWriteBits(code, length))
                    break;

                encoded++;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Пакетное кодирование в обратном порядке (как в Zstd).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int EncodeBatchReverse(
        ref BitWriter writer,
        ReadOnlySpan<byte> symbols,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var encoded = 0;

        fixed (byte* symPtr = symbols, lenPtr = lengths)
        fixed (uint* codePtr = codes)
        {
            for (var i = symbols.Length - 1; i >= 0; i--)
            {
                var symbol = symPtr[i];
                var code = codePtr[symbol];
                var length = lenPtr[symbol];

                if (length == 0 || !writer.TryWriteBits(code, length))
                    break;

                encoded++;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Пакетное кодирование с развёрнутым циклом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int EncodeBatchUnrolled(
        ref BitWriter writer,
        ReadOnlySpan<byte> symbols,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var encoded = 0;
        var count = symbols.Length;

        fixed (byte* symPtr = symbols)
        fixed (uint* codePtr = codes)
        fixed (byte* lenPtr = lengths)
        {
            // Развёрнутый цикл: 4 символа
            while (encoded + 4 <= count)
            {
                var s0 = symPtr[encoded];
                var s1 = symPtr[encoded + 1];
                var s2 = symPtr[encoded + 2];
                var s3 = symPtr[encoded + 3];

                var l0 = lenPtr[s0];
                var l1 = lenPtr[s1];
                var l2 = lenPtr[s2];
                var l3 = lenPtr[s3];

                // Проверяем, что все символы валидны
                if (l0 == 0 || l1 == 0 || l2 == 0 || l3 == 0)
                    break;

                // Пробуем записать все 4 символа
                if (!writer.TryWriteBits(codePtr[s0], l0)) break;
                if (!writer.TryWriteBits(codePtr[s1], l1)) break;
                if (!writer.TryWriteBits(codePtr[s2], l2)) break;
                if (!writer.TryWriteBits(codePtr[s3], l3)) break;

                encoded += 4;
            }

            // Остаток
            while (encoded < count)
            {
                var s = symPtr[encoded];
                var l = lenPtr[s];
                var c = codePtr[s];

                if (l == 0 || !writer.TryWriteBits(c, l))
                    break;

                encoded++;
            }
        }

        return encoded;
    }

    #endregion

    #region Stream Encoding (Zstd style)

    /// <summary>
    /// Кодирует данные в reverse bitstream с padding (как в Zstd).
    /// </summary>
    /// <param name="symbols">Входные символы.</param>
    /// <param name="destination">Выходной буфер.</param>
    /// <param name="codes">Массив кодов.</param>
    /// <param name="lengths">Массив длин.</param>
    /// <returns>Количество записанных байт, или 0 при ошибке.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int EncodeReverseStream(
        ReadOnlySpan<byte> symbols,
        Span<byte> destination,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths)
    {
        var writer = new BitWriter(destination, lsbFirst: true);

        // Кодируем в обратном порядке
        for (var i = symbols.Length - 1; i >= 0; i--)
        {
            var symbol = symbols[i];
            var length = lengths[symbol];

            if (length == 0)
                return 0; // Невалидный символ

            if (!writer.TryWriteBits(codes[symbol], length))
                return 0; // Overflow
        }

        // Добавляем padding bit
        if (!writer.TryFinishWithPadding())
            return 0;

        return writer.BytesWritten;
    }

    /// <summary>
    /// Кодирует данные в 4 interleaved потока (как в Zstd compressed literals).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Encode4Streams(
        ReadOnlySpan<byte> symbols,
        Span<byte> destination,
        ReadOnlySpan<uint> codes,
        ReadOnlySpan<byte> lengths,
        out int stream1Size,
        out int stream2Size,
        out int stream3Size)
    {
        var segmentSize = (symbols.Length + 3) / 4;

        // Вычисляем границы сегментов
        var seg1End = Math.Min(segmentSize, symbols.Length);
        var seg2End = Math.Min(segmentSize * 2, symbols.Length);
        var seg3End = Math.Min(segmentSize * 3, symbols.Length);

        var seg1 = symbols[..seg1End];
        var seg2 = symbols[seg1End..seg2End];
        var seg3 = symbols[seg2End..seg3End];
        var seg4 = symbols[seg3End..];

        // Кодируем каждый сегмент
        var offset = 0;

        // Stream 1
        stream1Size = EncodeReverseStream(seg1, destination[offset..], codes, lengths);
        if (stream1Size == 0) { stream2Size = stream3Size = 0; return 0; }
        offset += stream1Size;

        // Stream 2
        stream2Size = EncodeReverseStream(seg2, destination[offset..], codes, lengths);
        if (stream2Size == 0) { stream3Size = 0; return 0; }
        offset += stream2Size;

        // Stream 3
        stream3Size = EncodeReverseStream(seg3, destination[offset..], codes, lengths);
        if (stream3Size == 0) return 0;
        offset += stream3Size;

        // Stream 4
        var stream4Size = EncodeReverseStream(seg4, destination[offset..], codes, lengths);
        if (stream4Size == 0) return 0;

        return offset + stream4Size;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Вычисляет размер закодированных данных в битах.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateEncodedBits(
        ReadOnlySpan<byte> symbols,
        ReadOnlySpan<byte> lengths)
    {
        var totalBits = 0;

        for (var i = 0; i < symbols.Length; i++)
        {
            var length = lengths[symbols[i]];
            if (length == 0) return -1; // Невалидный символ
            totalBits += length;
        }

        return totalBits;
    }

    /// <summary>
    /// Вычисляет размер закодированных данных в байтах (с учётом padding).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateEncodedBytes(
        ReadOnlySpan<byte> symbols,
        ReadOnlySpan<byte> lengths)
    {
        var bits = CalculateEncodedBits(symbols, lengths);
        if (bits < 0) return -1;

        // +1 для padding bit, затем округляем вверх до байта
        return (bits + 1 + 7) / 8;
    }

    #endregion
}
