#pragma warning disable CA1000, CA1062, CA2208, S3776, MA0015, S109, MA0051

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// Построение таблиц Хаффмана из различных источников.
/// </summary>
/// <remarks>
/// Поддерживает:
/// - Построение из code lengths (DEFLATE, VP8L, JPEG)
/// - Построение из частот символов
/// - Canonical Huffman codes
/// - Package-Merge для ограничения глубины
///
/// Реализация с unsafe pointer-арифметикой для максимальной производительности.
/// </remarks>
public static unsafe class HuffmanTreeBuilder
{
    #region Constants

    /// <summary>Максимальная длина кода по умолчанию.</summary>
    public const int DefaultMaxCodeLength = 15;

    /// <summary>Максимальный размер алфавита.</summary>
    public const int MaxAlphabetSize = 288;

    #endregion

    #region Build from Code Lengths

    /// <summary>
    /// Строит таблицу декодирования из массива длин кодов (canonical Huffman).
    /// </summary>
    /// <param name="codeLengths">Длины кодов для каждого символа (0 = символ отсутствует).</param>
    /// <param name="symbols">Выходной буфер для символов (размер = 2^maxBits).</param>
    /// <param name="lengths">Выходной буфер для длин (размер = 2^maxBits).</param>
    /// <param name="maxBits">Максимальная длина кода (определяется автоматически если 0).</param>
    /// <param name="lsbFirst">True для LSB-first кодов (DEFLATE), false для MSB-first.</param>
    /// <returns>Фактическая максимальная длина кода.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildDecodeTable(
        ReadOnlySpan<byte> codeLengths,
        Span<byte> symbols,
        Span<byte> lengths,
        int maxBits = 0,
        bool lsbFirst = true)
    {
        // Определяем maxBits если не задан
        if (maxBits == 0)
        {
            for (var i = 0; i < codeLengths.Length; i++)
            {
                if (codeLengths[i] > maxBits)
                    maxBits = codeLengths[i];
            }
        }

        if (maxBits == 0) return 0; // Пустое дерево

        var tableSize = 1 << maxBits;

        // Подсчёт символов каждой длины (игнорируем len > maxBits для защиты от некорректных данных)
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len != 0 && len <= maxBits) blCount[len]++;
        }

        // Вычисляем первый код каждой длины (canonical)
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Инициализируем таблицу значениями по умолчанию
        fixed (byte* symPtr = symbols, lenPtr = lengths)
        {
            for (var i = 0; i < tableSize; i++)
            {
                symPtr[i] = 0;
                lenPtr[i] = (byte)maxBits;
            }
        }

        // Заполняем таблицу
        fixed (byte* symPtr = symbols, lenPtr = lengths, clPtr = codeLengths)
        {
            for (var sym = 0; sym < codeLengths.Length; sym++)
            {
                var len = clPtr[sym];
                if (len == 0 || len > maxBits) continue;

                var c = nextCode[len]++;
                var tableIndex = lsbFirst ? (int)ReverseBits(c, len) : c;
                var stride = 1 << len;

                for (var idx = tableIndex; idx < tableSize; idx += stride)
                {
                    symPtr[idx] = (byte)sym;
                    lenPtr[idx] = len;
                }
            }
        }

        return maxBits;
    }

    /// <summary>
    /// Строит таблицу декодирования из массива длин кодов в managed буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HuffmanTable BuildDecodeTable(
        ReadOnlySpan<byte> codeLengths,
        HuffmanTableBuffer buffer,
        bool lsbFirst = true)
    {
        _ = BuildDecodeTable(codeLengths, buffer.Symbols, buffer.Lengths, buffer.TableLog, lsbFirst);
        return buffer.ToTable();
    }

    /// <summary>
    /// Строит таблицу декодирования с 16-битными символами (для алфавитов > 256).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildDecodeTable16(
        ReadOnlySpan<byte> codeLengths,
        Span<ushort> symbols,
        Span<byte> lengths,
        int maxBits = 0,
        bool lsbFirst = true)
    {
        // Определяем maxBits если не задан
        if (maxBits == 0)
        {
            for (var i = 0; i < codeLengths.Length; i++)
            {
                if (codeLengths[i] > maxBits)
                    maxBits = codeLengths[i];
            }
        }

        if (maxBits == 0) return 0; // Пустое дерево

        var tableSize = 1 << maxBits;

        // Подсчёт символов каждой длины (игнорируем len > maxBits для защиты от некорректных данных)
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len != 0 && len <= maxBits) blCount[len]++;
        }

        // Вычисляем первый код каждой длины (canonical)
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Инициализируем таблицу значениями по умолчанию
        fixed (ushort* symPtr = symbols)
        fixed (byte* lenPtr = lengths)
        {
            for (var i = 0; i < tableSize; i++)
            {
                symPtr[i] = 0;
                lenPtr[i] = (byte)maxBits;
            }
        }

        // Заполняем таблицу
        fixed (ushort* symPtr = symbols)
        fixed (byte* lenPtr = lengths, clPtr = codeLengths)
        {
            for (var sym = 0; sym < codeLengths.Length; sym++)
            {
                var len = clPtr[sym];
                if (len == 0 || len > maxBits) continue;

                var c = nextCode[len]++;
                var tableIndex = lsbFirst ? (int)ReverseBits(c, len) : c;
                var stride = 1 << len;

                for (var idx = tableIndex; idx < tableSize; idx += stride)
                {
                    symPtr[idx] = (ushort)sym;
                    lenPtr[idx] = len;
                }
            }
        }

        return maxBits;
    }

    /// <summary>
    /// Строит 16-битную таблицу декодирования в managed буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HuffmanTable16 BuildDecodeTable16(
        ReadOnlySpan<byte> codeLengths,
        HuffmanTableBuffer16 buffer,
        bool lsbFirst = true)
    {
        _ = BuildDecodeTable16(codeLengths, buffer.Symbols, buffer.Lengths, buffer.TableLog, lsbFirst);
        return buffer.ToTable();
    }

    #endregion

    #region Build from Frequencies

    /// <summary>
    /// Строит коды Хаффмана из частот символов.
    /// </summary>
    /// <param name="frequencies">Частоты для каждого символа.</param>
    /// <param name="codeLengths">Выходной буфер для длин кодов.</param>
    /// <param name="maxCodeLength">Максимальная допустимая длина кода.</param>
    /// <returns>Максимальная использованная длина кода.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildFromFrequencies(
        ReadOnlySpan<uint> frequencies,
        Span<byte> codeLengths,
        int maxCodeLength = DefaultMaxCodeLength)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frequencies.Length, MaxAlphabetSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCodeLength, HuffmanTable.MaxCodeLength);

        // Очищаем выходной буфер
        codeLengths[..frequencies.Length].Clear();

        // Подсчитываем ненулевые символы с SIMD
        var symbolCount = SimdHistogram.CountNonZeroSymbols(frequencies);

        // Особые случаи
        if (symbolCount == 0) return 0;

        if (symbolCount == 1)
        {
            // Единственный символ получает код длины 1
            for (var i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] > 0)
                {
                    codeLengths[i] = 1;
                    return 1;
                }
            }
        }

        if (symbolCount == 2)
        {
            // Два символа — оба длины 1
            var found = 0;
            for (var i = 0; i < frequencies.Length && found < 2; i++)
            {
                if (frequencies[i] > 0)
                {
                    codeLengths[i] = 1;
                    found++;
                }
            }

            return 1;
        }

        // Используем Package-Merge для ограничения глубины
        return BuildWithPackageMerge(frequencies, codeLengths, maxCodeLength);
    }

    /// <summary>
    /// Package-Merge алгоритм для построения length-limited Huffman codes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int BuildWithPackageMerge(
        ReadOnlySpan<uint> frequencies,
        Span<byte> codeLengths,
        int maxCodeLength)
    {
        // Собираем активные символы
        Span<(int symbol, uint freq)> active = stackalloc (int, uint)[frequencies.Length];
        var activeCount = 0;

        for (var i = 0; i < frequencies.Length; i++)
        {
            if (frequencies[i] > 0)
            {
                active[activeCount++] = (i, frequencies[i]);
            }
        }

        // Сортируем по частоте (insertion sort для небольших массивов)
        SortByFrequency(active[..activeCount]);

        // Kraft inequality: sum(2^(-l_i)) = 1
        // Используем in-place построение через depth
        Span<int> depths = stackalloc int[activeCount];

        // Простой жадный алгоритм с ограничением глубины
        BuildDepthsGreedy(active[..activeCount], depths, maxCodeLength);

        // Записываем результат
        var maxUsed = 0;
        for (var i = 0; i < activeCount; i++)
        {
            var sym = active[i].symbol;
            var depth = depths[i];
            codeLengths[sym] = (byte)depth;
            if (depth > maxUsed) maxUsed = depth;
        }

        return maxUsed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SortByFrequency(Span<(int symbol, uint freq)> items)
    {
        // Insertion sort — эффективен для небольших массивов
        for (var i = 1; i < items.Length; i++)
        {
            var key = items[i];
            var j = i - 1;

            while (j >= 0 && items[j].freq > key.freq)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = key;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BuildDepthsGreedy(
        ReadOnlySpan<(int symbol, uint freq)> sorted,
        Span<int> depths,
        int maxDepth)
    {
        var n = sorted.Length;

        for (var i = 0; i < n; i++)
            depths[i] = 0;

        if (n <= 1)
        {
            if (n == 1) depths[0] = 1;
            return;
        }

        // Строим дерево Хаффмана и вычисляем глубины
        BuildHuffmanTree(sorted, depths, n);

        // Ограничиваем глубины и корректируем для Kraft inequality
        LimitAndAdjustDepths(sorted, depths, n, maxDepth);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BuildHuffmanTree(
        ReadOnlySpan<(int symbol, uint freq)> sorted,
        Span<int> depths,
        int n)
    {
        // Массив узлов: (weight, left, right, isLeaf)
        Span<(ulong weight, int left, int right, bool isLeaf)> nodes = stackalloc (ulong, int, int, bool)[(n * 2) - 1];
        var nodeCount = 0;

        // Инициализируем листья
        for (var i = 0; i < n; i++)
            nodes[nodeCount++] = (sorted[i].freq, i, -1, true);

        // Активные узлы для построения дерева
        Span<int> activeNodes = stackalloc int[n];
        for (var i = 0; i < n; i++)
            activeNodes[i] = i;

        var activeCount = n;

        // Строим дерево: объединяем два минимальных узла
        while (activeCount > 1)
        {
            FindTwoMinimum(nodes, activeNodes, activeCount, out var min1Idx, out var min2Idx);

            var left = activeNodes[min1Idx];
            var right = activeNodes[min2Idx];
            var newWeight = nodes[left].weight + nodes[right].weight;
            var newNode = nodeCount++;
            nodes[newNode] = (newWeight, left, right, false);

            // Обновляем активные узлы
            if (min1Idx > min2Idx) (min1Idx, min2Idx) = (min2Idx, min1Idx);
            activeNodes[min2Idx] = activeNodes[activeCount - 1];
            activeNodes[min1Idx] = newNode;
            activeCount--;
        }

        // Вычисляем глубины обходом дерева
        ComputeDepthsFromTree(nodes, activeNodes[0], depths, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FindTwoMinimum(
        ReadOnlySpan<(ulong weight, int left, int right, bool isLeaf)> nodes,
        ReadOnlySpan<int> activeNodes,
        int activeCount,
        out int min1Idx,
        out int min2Idx)
    {
        min1Idx = 0;
        min2Idx = 1;
        if (nodes[activeNodes[min1Idx]].weight > nodes[activeNodes[min2Idx]].weight)
            (min1Idx, min2Idx) = (min2Idx, min1Idx);

        for (var i = 2; i < activeCount; i++)
        {
            var w = nodes[activeNodes[i]].weight;
            if (w < nodes[activeNodes[min1Idx]].weight)
            {
                min2Idx = min1Idx;
                min1Idx = i;
            }
            else if (w < nodes[activeNodes[min2Idx]].weight)
            {
                min2Idx = i;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeDepthsFromTree(
        ReadOnlySpan<(ulong weight, int left, int right, bool isLeaf)> nodes,
        int root,
        Span<int> depths,
        int n)
    {
        Span<(int nodeIdx, int depth)> stack = stackalloc (int, int)[(n * 2)];
        var stackTop = 0;
        stack[stackTop++] = (root, 0);

        while (stackTop > 0)
        {
            var (nodeIdx, depth) = stack[--stackTop];
            var (_, left, right, isLeaf) = nodes[nodeIdx];

            if (isLeaf)
            {
                depths[left] = depth == 0 ? 1 : depth;
            }
            else
            {
                stack[stackTop++] = (left, depth + 1);
                stack[stackTop++] = (right, depth + 1);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LimitAndAdjustDepths(
        ReadOnlySpan<(int symbol, uint freq)> sorted,
        Span<int> depths,
        int n,
        int maxDepth)
    {
        var needsAdjustment = false;
        for (var i = 0; i < n; i++)
        {
            if (depths[i] > maxDepth)
            {
                depths[i] = maxDepth;
                needsAdjustment = true;
            }
            if (depths[i] == 0) depths[i] = 1;
        }

        if (needsAdjustment)
            AdjustDepthsForKraft(sorted, depths, n, maxDepth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustDepthsForKraft(
        ReadOnlySpan<(int symbol, uint freq)> sorted,
        Span<int> depths,
        int n,
        int maxDepth)
    {
        var kraftLimit = 1u << maxDepth;
        var kraftSum = 0u;

        for (var i = 0; i < n; i++)
            kraftSum += 1u << (maxDepth - depths[i]);

        while (kraftSum > kraftLimit)
        {
            var minIdx = -1;
            var minFreq = uint.MaxValue;

            for (var i = 0; i < n; i++)
            {
                if (depths[i] < maxDepth && sorted[i].freq < minFreq)
                {
                    minFreq = sorted[i].freq;
                    minIdx = i;
                }
            }

            if (minIdx < 0) break;

            var oldContrib = 1u << (maxDepth - depths[minIdx]);
            depths[minIdx]++;
            var newContrib = 1u << (maxDepth - depths[minIdx]);
            kraftSum = kraftSum - oldContrib + newContrib;
        }
    }

    #endregion

    #region Build Encode Table

    /// <summary>
    /// Строит таблицу кодирования из длин кодов.
    /// </summary>
    /// <param name="codeLengths">Длины кодов.</param>
    /// <param name="codes">Выходной буфер для кодов (parallel array).</param>
    /// <param name="lsbFirst">True для LSB-first кодов.</param>
    /// <returns>Максимальная длина кода.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildEncodeCodes(
        ReadOnlySpan<byte> codeLengths,
        Span<uint> codes,
        bool lsbFirst = true)
    {
        // Находим maxBits
        var maxBits = 0;
        for (var i = 0; i < codeLengths.Length; i++)
        {
            if (codeLengths[i] > maxBits)
                maxBits = codeLengths[i];
        }

        if (maxBits == 0) return 0;

        // Подсчёт символов каждой длины
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len != 0) blCount[len]++;
        }

        // Первый код каждой длины
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Назначаем коды
        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len == 0)
            {
                codes[i] = 0;
                continue;
            }

            var c = (uint)nextCode[len]++;
            codes[i] = lsbFirst ? ReverseBits((int)c, len) : c;
        }

        return maxBits;
    }

    /// <summary>
    /// Строит полные HuffmanCode структуры.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BuildEncodeCodes(
        ReadOnlySpan<byte> codeLengths,
        Span<HuffmanCode> huffmanCodes,
        bool lsbFirst = true)
    {
        Span<uint> codes = stackalloc uint[codeLengths.Length];
        var maxBits = BuildEncodeCodes(codeLengths, codes, lsbFirst);

        for (var i = 0; i < codeLengths.Length; i++)
        {
            huffmanCodes[i] = new HuffmanCode((ushort)i, codes[i], codeLengths[i]);
        }

        return maxBits;
    }

    #endregion

    #region Build from Raw Data

    /// <summary>
    /// Строит коды Хаффмана напрямую из сырых данных.
    /// </summary>
    /// <param name="data">Входные данные для подсчёта частот.</param>
    /// <param name="codeLengths">Выходной буфер для длин кодов (размер минимум 256).</param>
    /// <param name="maxCodeLength">Максимальная допустимая длина кода.</param>
    /// <returns>Максимальная использованная длина кода.</returns>
    /// <remarks>
    /// Использует SIMD-оптимизированный подсчёт гистограммы.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int BuildFromData(
        ReadOnlySpan<byte> data,
        Span<byte> codeLengths,
        int maxCodeLength = DefaultMaxCodeLength)
    {
        if (data.IsEmpty)
        {
            codeLengths[..256].Clear();
            return 0;
        }

        // Подсчитываем гистограмму с SIMD
        Span<uint> frequencies = stackalloc uint[256];
        SimdHistogram.ComputeHistogram(data, frequencies);

        // Строим Huffman из частот
        return BuildFromFrequencies(frequencies, codeLengths, maxCodeLength);
    }

    /// <summary>
    /// Строит коды Хаффмана напрямую из данных в managed буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int BuildFromData(
        ReadOnlySpan<byte> data,
        Span<byte> codeLengths,
        Span<uint> codes,
        int maxCodeLength = DefaultMaxCodeLength,
        bool lsbFirst = true)
    {
        var maxBits = BuildFromData(data, codeLengths, maxCodeLength);
        if (maxBits > 0)
        {
            _ = BuildEncodeCodes(codeLengths, codes, lsbFirst);
        }

        return maxBits;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Обращает порядок бит в числе.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReverseBits(int value, int bitCount)
    {
        var v = (uint)value;
        v = ((v & 0x5555u) << 1) | ((v >> 1) & 0x5555u);
        v = ((v & 0x3333u) << 2) | ((v >> 2) & 0x3333u);
        v = ((v & 0x0F0Fu) << 4) | ((v >> 4) & 0x0F0Fu);
        v = ((v & 0x00FFu) << 8) | ((v >> 8) & 0x00FFu);
        return v >> (16 - bitCount);
    }

    /// <summary>
    /// Проверяет валидность набора длин кодов (Kraft inequality).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ValidateCodeLengths(ReadOnlySpan<byte> codeLengths, int maxBits = 0)
    {
        if (maxBits == 0)
        {
            for (var i = 0; i < codeLengths.Length; i++)
            {
                if (codeLengths[i] > maxBits)
                    maxBits = codeLengths[i];
            }
        }

        if (maxBits == 0) return true; // Пустое дерево валидно

        // Kraft inequality: sum(2^(maxBits - len)) должна быть = 2^maxBits
        var sum = 0u;
        var maxSum = 1u << maxBits;

        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len == 0) continue;
            if (len > maxBits) return false;

            sum += 1u << (maxBits - len);
        }

        return sum == maxSum;
    }

    /// <summary>
    /// Вычисляет floor(log2(n)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FloorLog2(int value) =>
        31 - BitOperations.LeadingZeroCount((uint)value);

    /// <summary>
    /// Вычисляет ceil(log2(n)).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CeilLog2(int value) =>
        32 - BitOperations.LeadingZeroCount((uint)(value - 1));

    #endregion
}
