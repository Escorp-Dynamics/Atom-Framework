#pragma warning disable CA1000, CA1062, CA2208, S3776, MA0015, S109, MA0051, IDE0007

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
/// Реализация с Unsafe ref-арифметикой для максимальной производительности.
/// </remarks>
public static class HuffmanTreeBuilder
{
    #region Constants

    /// <summary>Максимальная длина кода по умолчанию.</summary>
    public const int DefaultMaxCodeLength = 15;

    /// <summary>Максимальный размер алфавита (VP8L с максимальным color cache: 256 + 24 + 2048).</summary>
    public const int MaxAlphabetSize = 2328;

    /// <summary>Порог для stackalloc: алфавиты больше этого значения аллоцируются в куче.</summary>
    private const int StackAllocThreshold = 512;

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
        symbols[..tableSize].Clear();
        var defaultLen = (byte)maxBits;
        lengths[..tableSize].Fill(defaultLen);

        ref var symRef = ref MemoryMarshal.GetReference(symbols);
        ref var lenRef = ref MemoryMarshal.GetReference(lengths);

        // Заполняем таблицу
        ref var clRef = ref MemoryMarshal.GetReference(codeLengths);
        for (var sym = 0; sym < codeLengths.Length; sym++)
        {
            var len = Unsafe.Add(ref clRef, sym);
            if (len == 0 || len > maxBits) continue;

            var c = nextCode[len]++;
            var tableIndex = lsbFirst ? (int)ReverseBits(c, len) : c;
            var stride = 1 << len;

            for (var idx = tableIndex; idx < tableSize; idx += stride)
            {
                Unsafe.Add(ref symRef, idx) = (byte)sym;
                Unsafe.Add(ref lenRef, idx) = len;
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
        symbols[..tableSize].Clear();
        var defaultLen16 = (byte)maxBits;
        lengths[..tableSize].Fill(defaultLen16);

        ref var symRef16 = ref MemoryMarshal.GetReference(symbols);
        ref var lenRef16 = ref MemoryMarshal.GetReference(lengths);

        // Заполняем таблицу
        ref var clRef16 = ref MemoryMarshal.GetReference(codeLengths);
        for (var sym = 0; sym < codeLengths.Length; sym++)
        {
            var len = Unsafe.Add(ref clRef16, sym);
            if (len == 0 || len > maxBits) continue;

            var c = nextCode[len]++;
            var tableIndex = lsbFirst ? (int)ReverseBits(c, len) : c;
            var stride = 1 << len;

            for (var idx = tableIndex; idx < tableSize; idx += stride)
            {
                Unsafe.Add(ref symRef16, idx) = (ushort)sym;
                Unsafe.Add(ref lenRef16, idx) = len;
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
        // Собираем активные символы (heap fallback для больших алфавитов)
        Span<(int symbol, uint freq)> active = frequencies.Length <= StackAllocThreshold
            ? stackalloc (int, uint)[frequencies.Length]
            : new (int, uint)[frequencies.Length];
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
        // Используем in-place построение через depth (heap fallback для больших алфавитов)
        Span<int> depths = activeCount <= StackAllocThreshold
            ? stackalloc int[activeCount]
            : new int[activeCount];

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
        if (items.Length <= 32)
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
        else
        {
            // Introsort для больших алфавитов (VP8L: до 2328 символов)
            items.Sort(static (a, b) => a.freq.CompareTo(b.freq));
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
        LimitAndAdjustDepths(depths, n, maxDepth);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BuildHuffmanTree(
        ReadOnlySpan<(int symbol, uint freq)> sorted,
        Span<int> depths,
        int n)
    {
        // Two-queue подход: O(n) вместо O(n²).
        // Queue 1 = отсортированные листья (sorted[]), Queue 2 = внутренние узлы.
        // Внутренние узлы тоже отсортированы по весу (сумма двух наименьших ≤ следующей суммы).
        // Merging двух очередей для поиска минимума — O(1) за шаг.
        // Heap fallback для больших алфавитов (VP8L color cache: до 2328 символов)
        Span<ulong> internalWeight = n - 1 <= StackAllocThreshold
            ? stackalloc ulong[n - 1]
            : new ulong[n - 1];
        // parent[i] для i < n — лист, для i >= n — внутренний узел (i - n)
        var parentSize = (2 * n) - 1;
        Span<int> parent = parentSize <= StackAllocThreshold
            ? stackalloc int[parentSize]
            : new int[parentSize];
        parent.Fill(-1);

        var leafIdx = 0;   // голова очереди листьев
        var intHead = 0;   // голова очереди внутренних узлов
        var intTail = 0;   // хвост очереди внутренних узлов

        for (var step = 0; step < n - 1; step++)
        {
            // Выбираем первый минимальный узел
            ulong w1;
            int node1;
            if (intHead < intTail && (leafIdx >= n || internalWeight[intHead] <= sorted[leafIdx].freq))
            {
                w1 = internalWeight[intHead];
                node1 = n + intHead;
                intHead++;
            }
            else
            {
                w1 = sorted[leafIdx].freq;
                node1 = leafIdx;
                leafIdx++;
            }

            // Выбираем второй минимальный узел
            ulong w2;
            int node2;
            if (intHead < intTail && (leafIdx >= n || internalWeight[intHead] <= sorted[leafIdx].freq))
            {
                w2 = internalWeight[intHead];
                node2 = n + intHead;
                intHead++;
            }
            else
            {
                w2 = sorted[leafIdx].freq;
                node2 = leafIdx;
                leafIdx++;
            }

            // Создаём внутренний узел
            internalWeight[intTail] = w1 + w2;
            var parentNode = n + intTail;
            parent[node1] = parentNode;
            parent[node2] = parentNode;
            intTail++;
        }

        // Вычисляем глубины: top-down от корня (один проход)
        // depth[i] для i < n — лист, для i >= n — внутренний узел
        Span<int> nodeDepth = parentSize <= StackAllocThreshold
            ? stackalloc int[parentSize]
            : new int[parentSize];

        // Корень — последний внутренний узел: n + (n-2)
        var root = (2 * n) - 2;
        nodeDepth[root] = 0;

        // Пройти все узлы и проставить depth = parent.depth + 1
        // Внутренние узлы идут в порядке создания, parent[node] всегда > node
        // ⇒ обрабатываем в обратном порядке от корня
        for (var node = root - 1; node >= 0; node--)
        {
            if (parent[node] >= 0)
            {
                nodeDepth[node] = nodeDepth[parent[node]] + 1;
            }
        }

        for (var i = 0; i < n; i++)
        {
            depths[i] = Math.Max(nodeDepth[i], 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LimitAndAdjustDepths(
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
            AdjustDepthsForKraft(depths, n, maxDepth);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void AdjustDepthsForKraft(
        Span<int> depths,
        int n,
        int maxDepth)
    {
        var kraftLimit = 1u << maxDepth;
        var kraftSum = 0u;

        for (var i = 0; i < n; i++)
        {
            kraftSum += 1u << (maxDepth - depths[i]);
        }

        // Фаза 1: Oversubscribed (kraftSum > kraftLimit) — увеличиваем depth наименее частых символов
        for (var i = 0; i < n && kraftSum > kraftLimit; i++)
        {
            while (depths[i] < maxDepth && kraftSum > kraftLimit)
            {
                var oldContrib = 1u << (maxDepth - depths[i]);
                depths[i]++;
                var newContrib = 1u << (maxDepth - depths[i]);
                kraftSum = kraftSum - oldContrib + newContrib;
            }
        }

        // Фаза 2: Underfull (kraftSum < kraftLimit) — уменьшаем depth наиболее глубоких символов
        // Итерируем от конца (наибольшая частота / наибольший depth после фазы 1)
        for (var i = n - 1; i >= 0 && kraftSum < kraftLimit; i--)
        {
            while (depths[i] > 1 && kraftSum < kraftLimit)
            {
                var oldContrib = 1u << (maxDepth - depths[i]);
                var newContrib = 1u << (maxDepth - (depths[i] - 1));
                var delta = newContrib - oldContrib;

                if (kraftSum + delta <= kraftLimit)
                {
                    depths[i]--;
                    kraftSum += delta;
                }
                else
                {
                    break;
                }
            }
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
        ref var clRef = ref MemoryMarshal.GetReference(codeLengths);
        ref var codeRef = ref MemoryMarshal.GetReference(codes);
        var n = codeLengths.Length;

        // Находим maxBits
        var maxBits = 0;
        for (var i = 0; i < n; i++)
        {
            var len = Unsafe.Add(ref clRef, i);
            if (len > maxBits)
            {
                maxBits = len;
            }
        }

        if (maxBits == 0) return 0;

        // Подсчёт символов каждой длины
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var i = 0; i < n; i++)
        {
            var len = Unsafe.Add(ref clRef, i);
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
        for (var i = 0; i < n; i++)
        {
            var len = Unsafe.Add(ref clRef, i);
            if (len == 0)
            {
                Unsafe.Add(ref codeRef, i) = 0;
                continue;
            }

            var c = (uint)nextCode[len]++;
            Unsafe.Add(ref codeRef, i) = lsbFirst ? ReverseBits((int)c, len) : c;
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
