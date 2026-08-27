using System.Numerics;
using System.Runtime.CompilerServices;
using Atom.IO.Compression.Huffman;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Префиксные коды литералов Zstandard: построение таблицы декодирования по весам и чтение потоков.
/// </summary>
/// <remarks>
/// <para>
/// RFC 8878, §4.2.1.3: символы сортируются по весу, внутри одного веса сохраняют естественный порядок,
/// а коды раздаются подряд <b>начиная с наименьшего веса</b> (то есть с самого длинного кода).
/// Коды читаются старшим битом вперёд.
/// </para>
/// <para>
/// Это НЕ канон DEFLATE. В DEFLATE младшие коды получают самые короткие длины
/// (<c>code = (code + blCount[bits-1]) &lt;&lt; 1</c>), а таблица индексируется развёрнутыми битами.
/// Раньше блок литералов Zstd строился именно общим DEFLATE-построителем, из-за чего дерево
/// получалось зеркальным настоящему. Ошибка не ловилась, потому что собственный кодировщик Zstd
/// пользовался тем же построителем: круговой прогон сходился, а чужие потоки — нет.
/// </para>
/// </remarks>
internal static class ZstdHuffman
{
    /// <summary>Предел глубины дерева литералов (RFC 8878, §4.2.1).</summary>
    internal const int MaxTableLog = 11;

    /// <summary>Максимальное число символов алфавита литералов.</summary>
    internal const int MaxSymbolCount = 256;

    /// <summary>
    /// Достраивает последний вес и вычисляет глубину дерева.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §4.2.1: сумма <c>2^(вес-1)</c> по известным весам дополняется до ближайшей степени
    /// двойки; недостающая часть обязана сама быть степенью двойки и задаёт последний вес,
    /// а полная степень — <c>Max_Number_of_Bits</c>.
    /// </remarks>
    /// <param name="weights">Веса символов; элемент с индексом <paramref name="count"/> будет дописан.</param>
    /// <param name="count">Число уже прочитанных весов.</param>
    /// <param name="maxBits">Глубина дерева.</param>
    /// <returns>Полное число символов (с достроенным последним).</returns>
    public static int CompleteWeights(Span<byte> weights, int count, out int maxBits)
    {
        if (count <= 0) throw new InvalidDataException("Empty Huffman weights");
        if (count >= MaxSymbolCount) throw new InvalidDataException("Too many Huffman weights");

        var total = 0;
        for (var i = 0; i < count; i++)
        {
            var weight = weights[i];
            if (weight > MaxTableLog) throw new InvalidDataException("Huffman weight > 11");
            if (weight != 0) total += 1 << (weight - 1);
        }

        if (total == 0) throw new InvalidDataException("Huffman weights are all zero");

        var pow2 = (int)BitOperations.RoundUpToPowerOf2((uint)(total + 1));
        var remainder = pow2 - total;

        // Остаток обязан быть точной степенью двойки: иначе описание дерева повреждено.
        if (!BitOperations.IsPow2(remainder)) throw new InvalidDataException("Huffman weights do not complete to a power of 2");

        maxBits = BitOperations.Log2((uint)pow2);
        if (maxBits > MaxTableLog) throw new InvalidDataException("Huffman depth > 11");

        weights[count] = (byte)(BitOperations.Log2((uint)remainder) + 1);
        return count + 1;
    }

    /// <summary>
    /// Строит таблицу декодирования (индекс — старшие <paramref name="maxBits"/> бит потока).
    /// </summary>
    /// <param name="weights">Полный набор весов символов.</param>
    /// <param name="maxBits">Глубина дерева.</param>
    /// <param name="symbols">Выходной буфер символов размером не меньше 2^maxBits.</param>
    /// <param name="lengths">Выходной буфер длин кодов размером не меньше 2^maxBits.</param>
    public static void BuildDecodeTable(ReadOnlySpan<byte> weights, int maxBits, Span<byte> symbols, Span<byte> lengths)
    {
        var tableSize = 1 << maxBits;
        Span<int> rankStart = stackalloc int[MaxTableLog + 2];
        rankStart.Clear();

        // Сколько символов у каждого веса — веса растут, длины кодов убывают.
        for (var i = 0; i < weights.Length; i++)
        {
            var weight = weights[i];
            if (weight != 0) rankStart[weight]++;
        }

        var cursor = 0;
        for (var weight = 1; weight <= maxBits; weight++)
        {
            var occupied = rankStart[weight] << (weight - 1);
            rankStart[weight] = cursor;
            cursor += occupied;
        }

        if (cursor != tableSize) throw new InvalidDataException("Huffman table is not fully covered");

        symbols[..tableSize].Clear();
        lengths[..tableSize].Fill((byte)maxBits);

        for (var symbol = 0; symbol < weights.Length; symbol++)
        {
            var weight = weights[symbol];
            if (weight == 0) continue;

            var span = 1 << (weight - 1);
            var start = rankStart[weight];
            rankStart[weight] = start + span;
            symbols.Slice(start, span).Fill((byte)symbol);
            lengths.Slice(start, span).Fill((byte)(maxBits + 1 - weight));
        }
    }

    /// <summary>
    /// Декодирует один обратный поток литералов ровно в <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §4.2.2: поток читается с конца, а символы записываются <b>вперёд</b>, с нулевого
    /// индекса — обратный порядок уже заложен в чтение. Раньше здесь выход заполнялся с конца,
    /// что согласовывалось только с собственным кодировщиком.
    /// </remarks>
    /// <param name="stream">Байты потока.</param>
    /// <param name="destination">Приёмник ровно на нужное число литералов.</param>
    /// <param name="table">Таблица декодирования.</param>
    public static void DecodeStream(ReadOnlySpan<byte> stream, Span<byte> destination, in HuffmanTable table)
    {
        if (destination.IsEmpty) return;

        var reader = new ZstdReverseBitReader(stream);
        if (!reader.IsValid) throw new InvalidDataException("Malformed Huffman literals stream padding");

        var tableLog = table.TableLog;
        for (var i = 0; i < destination.Length; i++)
        {
            var peek = reader.PeekBitsPadded(tableLog);
            var symbol = table.DecodeSymbol(peek, out var length);
            if (!reader.TrySkipBits(length)) throw new InvalidDataException("Huffman literals stream underflow");
            destination[i] = symbol;
        }

        if (reader.AvailableBits != 0) throw new InvalidDataException("Huffman literals stream not fully consumed");
    }

    /// <summary>
    /// Декодирует четыре потока литералов в свои сегменты выходного буфера.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §3.1.1.3.1.6: размер каждого сегмента равен <c>(Regenerated_Size+3)/4</c>,
    /// кроме последнего — он может быть короче до трёх байт.
    /// </remarks>
    /// <param name="stream1">Первый поток.</param>
    /// <param name="stream2">Второй поток.</param>
    /// <param name="stream3">Третий поток.</param>
    /// <param name="stream4">Четвёртый поток.</param>
    /// <param name="destination">Приёмник ровно на Regenerated_Size байт.</param>
    /// <param name="table">Таблица декодирования.</param>
    public static void Decode4Streams(
        ReadOnlySpan<byte> stream1,
        ReadOnlySpan<byte> stream2,
        ReadOnlySpan<byte> stream3,
        ReadOnlySpan<byte> stream4,
        Span<byte> destination,
        in HuffmanTable table)
    {
        var total = destination.Length;
        var segment = (total + 3) / 4;
        if (total < segment * 3) throw new InvalidDataException("Regenerated size is too small for four streams");

        DecodeStream(stream1, destination[..segment], in table);
        DecodeStream(stream2, destination.Slice(segment, segment), in table);
        DecodeStream(stream3, destination.Slice(segment * 2, segment), in table);
        DecodeStream(stream4, destination[(segment * 3)..], in table);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static HuffmanTable Commit(ReadOnlySpan<byte> weights, int maxBits, ref ZstdDecoderWorkspace.HuffmanTableBlock block)
    {
        block.GetWorkspace(out var symbols, out var lengths);
        BuildDecodeTable(weights, maxBits, symbols, lengths);
        block.Commit(maxBits);
        return block.ToTable();
    }
}
