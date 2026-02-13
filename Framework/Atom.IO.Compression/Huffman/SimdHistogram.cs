#pragma warning disable CA1000, S3776, MA0051

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// SIMD-оптимизированный подсчёт гистограммы символов.
/// </summary>
/// <remarks>
/// Использует технику "частичных гистограмм" для эффективного
/// параллельного подсчёта на больших объёмах данных.
/// </remarks>
public static unsafe class SimdHistogram
{
    /// <summary>
    /// Порог для использования SIMD (меньшие массивы обрабатываются скалярно).
    /// </summary>
    private const int SimdThreshold = 256;

    /// <summary>
    /// Количество частичных гистограмм для уменьшения конфликтов записи.
    /// </summary>
    private const int PartialHistograms = 4;

    #region Public API

    /// <summary>
    /// Вычисляет гистограмму частот байтов.
    /// </summary>
    /// <param name="data">Входные данные.</param>
    /// <param name="histogram">Выходная гистограмма (должен быть размером минимум 256).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ComputeHistogram(ReadOnlySpan<byte> data, Span<uint> histogram)
    {
        if (histogram.Length < 256)
            throw new ArgumentException("Histogram must have at least 256 elements.", nameof(histogram));

        histogram[..256].Clear();

        if (data.IsEmpty)
            return;

        if (data.Length < SimdThreshold)
            ComputeHistogramScalar(data, histogram);
        else
            ComputeHistogramParallel(data, histogram);
    }

    /// <summary>
    /// Вычисляет гистограмму с добавлением к существующим значениям.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void AccumulateHistogram(ReadOnlySpan<byte> data, Span<uint> histogram)
    {
        if (histogram.Length < 256)
            throw new ArgumentException("Histogram must have at least 256 elements.", nameof(histogram));

        if (data.IsEmpty)
            return;

        if (data.Length < SimdThreshold)
            ComputeHistogramScalar(data, histogram);
        else
            ComputeHistogramParallel(data, histogram);
    }

    /// <summary>
    /// Подсчитывает количество ненулевых символов в гистограмме.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int CountNonZeroSymbols(ReadOnlySpan<uint> histogram)
    {
        var length = Math.Min(histogram.Length, 256);

        if (Avx2.IsSupported && length >= 8)
            return CountNonZeroAvx2(histogram, length);

        if (Sse2.IsSupported && length >= 4)
            return CountNonZeroSse2(histogram, length);

        return CountNonZeroScalar(histogram, length);
    }

    /// <summary>
    /// Находит максимальное значение в гистограмме и его индекс.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static (uint maxValue, int maxIndex) FindMax(ReadOnlySpan<uint> histogram)
    {
        if (histogram.IsEmpty)
            return (0, -1);

        var length = Math.Min(histogram.Length, 256);

        if (Avx2.IsSupported && length >= 8)
            return FindMaxAvx2(histogram, length);

        return FindMaxScalar(histogram, length);
    }

    /// <summary>
    /// Вычисляет сумму всех элементов гистограммы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong Sum(ReadOnlySpan<uint> histogram)
    {
        var length = Math.Min(histogram.Length, 256);

        if (Avx2.IsSupported && length >= 8)
            return SumAvx2(histogram, length);

        return SumScalar(histogram, length);
    }

    #endregion

    #region CountNonZero Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountNonZeroAvx2(ReadOnlySpan<uint> histogram, int length)
    {
        var count = 0;
        fixed (uint* ptr = histogram)
        {
            var i = 0;
            var zero = Vector256<uint>.Zero;

            for (; i + 8 <= length; i += 8)
            {
                var vec = Avx.LoadVector256(ptr + i);
                var cmp = Avx2.CompareEqual(vec, zero);
                var mask = unchecked((uint)Avx.MoveMask(cmp.AsSingle()));
                count += 8 - BitOperations.PopCount(mask);
            }

            for (; i < length; i++)
                if (ptr[i] != 0) count++;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountNonZeroSse2(ReadOnlySpan<uint> histogram, int length)
    {
        var count = 0;
        fixed (uint* ptr = histogram)
        {
            var i = 0;
            var zero = Vector128<uint>.Zero;

            for (; i + 4 <= length; i += 4)
            {
                var vec = Sse2.LoadVector128(ptr + i);
                var cmp = Sse2.CompareEqual(vec, zero);
                var mask = unchecked((uint)Sse.MoveMask(cmp.AsSingle()));
                count += 4 - BitOperations.PopCount(mask);
            }

            for (; i < length; i++)
                if (ptr[i] != 0) count++;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountNonZeroScalar(ReadOnlySpan<uint> histogram, int length)
    {
        var count = 0;
        for (var i = 0; i < length; i++)
            if (histogram[i] != 0) count++;
        return count;
    }

    #endregion

    #region FindMax Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint maxValue, int maxIndex) FindMaxAvx2(ReadOnlySpan<uint> histogram, int length)
    {
        fixed (uint* ptr = histogram)
        {
            var i = 0;
            var maxVec = Vector256<uint>.Zero;

            // Первый проход: находим максимум по вектору
            for (; i + 8 <= length; i += 8)
            {
                var vec = Avx.LoadVector256(ptr + i);
                maxVec = Avx2.Max(maxVec, vec);
            }

            // Редукция вектора до скаляра
            var maxValue = ReduceMaxVector256(maxVec);

            // Обрабатываем остаток
            for (; i < length; i++)
            {
                if (ptr[i] > maxValue)
                    maxValue = ptr[i];
            }

            // Второй проход: находим первый индекс с максимальным значением
            var maxIndex = FindFirstIndex(ptr, length, maxValue);

            return (maxValue, maxIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReduceMaxVector256(Vector256<uint> vec)
    {
        var upper = Avx2.ExtractVector128(vec, 1);
        var lower = vec.GetLower();
        var max128 = Sse41.Max(upper, lower);
        var shuffled = Sse2.Shuffle(max128, 0b10_11_00_01);
        max128 = Sse41.Max(max128, shuffled);
        shuffled = Sse2.Shuffle(max128, 0b01_00_11_10);
        max128 = Sse41.Max(max128, shuffled);
        return max128.ToScalar();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindFirstIndex(uint* ptr, int length, uint value)
    {
        for (var i = 0; i < length; i++)
        {
            if (ptr[i] == value)
                return i;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint maxValue, int maxIndex) FindMaxScalar(ReadOnlySpan<uint> histogram, int length)
    {
        var maxValue = 0u;
        var maxIndex = -1;

        for (var i = 0; i < length; i++)
        {
            if (histogram[i] > maxValue)
            {
                maxValue = histogram[i];
                maxIndex = i;
            }
        }

        return (maxValue, maxIndex);
    }

    #endregion

    #region Sum Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SumAvx2(ReadOnlySpan<uint> histogram, int length)
    {
        fixed (uint* ptr = histogram)
        {
            var i = 0;
            var sumVec = Vector256<ulong>.Zero;

            for (; i + 8 <= length; i += 8)
            {
                var vec = Avx.LoadVector256(ptr + i);

                // Расширяем нижние 4 uint до ulong
                var lo = Avx2.ConvertToVector256Int64(vec.GetLower()).AsUInt64();
                // Расширяем верхние 4 uint до ulong
                var hi = Avx2.ConvertToVector256Int64(Avx2.ExtractVector128(vec, 1)).AsUInt64();

                sumVec = Avx2.Add(sumVec, lo);
                sumVec = Avx2.Add(sumVec, hi);
            }

            // Редукция вектора
            var sum = ReduceSumVector256(sumVec);

            // Остаток
            for (; i < length; i++)
                sum += ptr[i];

            return sum;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReduceSumVector256(Vector256<ulong> vec)
    {
        var upper = Avx2.ExtractVector128(vec, 1);
        var lower = vec.GetLower();
        var sum128 = Sse2.Add(upper, lower);
        return sum128.GetElement(0) + sum128.GetElement(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SumScalar(ReadOnlySpan<uint> histogram, int length)
    {
        var sum = 0uL;
        for (var i = 0; i < length; i++)
            sum += histogram[i];
        return sum;
    }

    #endregion

    #region Histogram Implementation

    /// <summary>
    /// Скалярный подсчёт гистограммы (для малых данных или fallback).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ComputeHistogramScalar(ReadOnlySpan<byte> data, Span<uint> histogram)
    {
        var i = 0;
        var length = data.Length;

        ref var dataRef = ref MemoryMarshal.GetReference(data);
        ref var histRef = ref MemoryMarshal.GetReference(histogram);

        // 4 элемента за итерацию
        for (; i + 4 <= length; i += 4)
        {
            Unsafe.Add(ref histRef, Unsafe.Add(ref dataRef, i))++;
            Unsafe.Add(ref histRef, Unsafe.Add(ref dataRef, i + 1))++;
            Unsafe.Add(ref histRef, Unsafe.Add(ref dataRef, i + 2))++;
            Unsafe.Add(ref histRef, Unsafe.Add(ref dataRef, i + 3))++;
        }

        for (; i < length; i++)
            Unsafe.Add(ref histRef, Unsafe.Add(ref dataRef, i))++;
    }

    /// <summary>
    /// Параллельный подсчёт с частичными гистограммами.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ComputeHistogramParallel(ReadOnlySpan<byte> data, Span<uint> histogram)
    {
        // 4 частичных гистограммы по 256 элементов = 4KB (помещается в L1)
        Span<uint> partial = stackalloc uint[256 * PartialHistograms];
        partial.Clear();

        ref var dataRef = ref MemoryMarshal.GetReference(data);
        ref var p0 = ref MemoryMarshal.GetReference(partial);
        ref var p1 = ref Unsafe.Add(ref p0, 256);
        ref var p2 = ref Unsafe.Add(ref p0, 512);
        ref var p3 = ref Unsafe.Add(ref p0, 768);

        var i = 0;
        var length = data.Length;

        // Обрабатываем по 4 байта, распределяя по 4 гистограммам
        for (; i + 4 <= length; i += 4)
        {
            Unsafe.Add(ref p0, Unsafe.Add(ref dataRef, i))++;
            Unsafe.Add(ref p1, Unsafe.Add(ref dataRef, i + 1))++;
            Unsafe.Add(ref p2, Unsafe.Add(ref dataRef, i + 2))++;
            Unsafe.Add(ref p3, Unsafe.Add(ref dataRef, i + 3))++;
        }

        // Остаток — в первую гистограмму
        for (; i < length; i++)
            Unsafe.Add(ref p0, Unsafe.Add(ref dataRef, i))++;

        // Объединяем частичные гистограммы с SIMD
        MergeHistograms(partial, histogram);
    }

    /// <summary>
    /// Объединяет 4 частичные гистограммы в одну с использованием SIMD.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void MergeHistograms(ReadOnlySpan<uint> partial, Span<uint> result)
    {
        fixed (uint* src = partial, dst = result)
        {
            var p0 = src;
            var p1 = src + 256;
            var p2 = src + 512;
            var p3 = src + 768;

            if (Avx2.IsSupported)
                MergeHistogramsAvx2(p0, p1, p2, p3, dst);
            else if (Sse2.IsSupported)
                MergeHistogramsSse2(p0, p1, p2, p3, dst);
            else
                MergeHistogramsScalar(p0, p1, p2, p3, dst);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MergeHistogramsAvx2(uint* p0, uint* p1, uint* p2, uint* p3, uint* dst)
    {
        for (var i = 0; i < 256; i += 8)
        {
            var v0 = Avx.LoadVector256(p0 + i);
            var v1 = Avx.LoadVector256(p1 + i);
            var v2 = Avx.LoadVector256(p2 + i);
            var v3 = Avx.LoadVector256(p3 + i);

            var sum01 = Avx2.Add(v0, v1);
            var sum23 = Avx2.Add(v2, v3);
            var existing = Avx.LoadVector256(dst + i);
            var total = Avx2.Add(Avx2.Add(sum01, sum23), existing);

            Avx.Store(dst + i, total);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MergeHistogramsSse2(uint* p0, uint* p1, uint* p2, uint* p3, uint* dst)
    {
        for (var i = 0; i < 256; i += 4)
        {
            var v0 = Sse2.LoadVector128(p0 + i);
            var v1 = Sse2.LoadVector128(p1 + i);
            var v2 = Sse2.LoadVector128(p2 + i);
            var v3 = Sse2.LoadVector128(p3 + i);

            var sum01 = Sse2.Add(v0, v1);
            var sum23 = Sse2.Add(v2, v3);
            var existing = Sse2.LoadVector128(dst + i);
            var total = Sse2.Add(Sse2.Add(sum01, sum23), existing);

            Sse2.Store(dst + i, total);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MergeHistogramsScalar(uint* p0, uint* p1, uint* p2, uint* p3, uint* dst)
    {
        for (var i = 0; i < 256; i++)
            dst[i] += p0[i] + p1[i] + p2[i] + p3[i];
    }

    #endregion
}
