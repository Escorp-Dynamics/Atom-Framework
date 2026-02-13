#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Rgb24.
/// Swap первого и третьего байта (B ↔ R) — чистый shuffle без вычислений.
/// </summary>
public readonly partial struct Bgr24
{


    #region Single Pixel Conversion (Rgb24)

    /// <summary>Конвертирует Rgb24 в Bgr24 (swap R и B).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromRgb24(Rgb24 rgb) => new(rgb.B, rgb.G, rgb.R);

    /// <summary>Конвертирует Bgr24 в Rgb24 (swap B и R).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24() => new(R, G, B);

    #endregion

    #region Batch Conversion (Bgr24 ↔ Rgb24)

    /// <summary>
    /// Реализованные ускорители для конвертации Bgr24 ↔ Rgb24.
    /// </summary>
    /// <remarks>
    /// AVX2 использует 8x unroll SSE128 операций для максимальной пропускной способности.
    /// Хотя AVX2.Shuffle работает только внутри 128-bit lanes, 8x unroll даёт лучший ILP.
    /// </remarks>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgr24 с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Bgr24> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgr24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Rgb24.</param>
    /// <param name="destination">Целевой буфер Bgr24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        // Параллельная обработка для буферов >= 1024 пикселей
        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgb24* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
            {
                FromRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgb24 с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Bgr24> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgb24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgr24.</param>
    /// <param name="destination">Целевой буфер Rgb24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void ToRgb24(ReadOnlySpan<Bgr24> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        // Параллельная обработка для буферов >= 1024 пикселей
        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (Rgb24* dstPtr = destination)
            {
                ToRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToRgb24Core(source, destination, selected);
    }

    #endregion

    #region Parallel Processing

    /// <summary>Однопоточная конвертация Rgb24 → Bgr24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            // BGR ↔ RGB — идентичная операция (swap B и R)
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 32:
                    SwapRBAvx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 20:
                    SwapRBAvx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 5:
                    SwapRBSsse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    SwapRBScalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Однопоточная конвертация Bgr24 → Rgb24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgb24Core(ReadOnlySpan<Bgr24> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            // BGR ↔ RGB — идентичная операция (swap B и R)
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 32:
                    SwapRBAvx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 20:
                    SwapRBAvx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 5:
                    SwapRBSsse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    SwapRBScalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Параллельная конвертация Rgb24 → Bgr24 с выбранным ускорителем.</summary>
    private static unsafe void FromRgb24Parallel(Rgb24* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgb24Core(new ReadOnlySpan<Rgb24>(source + start, size), new Span<Bgr24>(destination + start, size), selected);
        });
    }

    /// <summary>Параллельная конвертация Bgr24 → Rgb24 с выбранным ускорителем.</summary>
    private static unsafe void ToRgb24Parallel(Bgr24* source, Rgb24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgb24Core(new ReadOnlySpan<Bgr24>(source + start, size), new Span<Rgb24>(destination + start, size), selected);
        });
    }

    #endregion

    #region SIMD Implementations

    /// <summary>
    /// AVX512BW: 40 пикселей (120 байт) за итерацию с SSE shuffle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRBAvx512(byte* src, byte* dst, int pixelCount)
    {
        var byteCount = pixelCount * 3;
        var i = 0;

        var shuffleMask = Bgr24Sse41Vectors.SwapRB24ShuffleMask;
        const int BytesPerVector = 15;

        // 8x unroll: 40 пикселей за итерацию (120 байт)
        // ВАЖНО: Последний store записывает байты 105-120, поэтому нужно +1 запас
        while (i + (8 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));
            var v4 = Sse2.LoadVector128(src + i + (4 * BytesPerVector));
            var v5 = Sse2.LoadVector128(src + i + (5 * BytesPerVector));
            var v6 = Sse2.LoadVector128(src + i + (6 * BytesPerVector));
            var v7 = Sse2.LoadVector128(src + i + (7 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);
            var r4 = Ssse3.Shuffle(v4, shuffleMask);
            var r5 = Ssse3.Shuffle(v5, shuffleMask);
            var r6 = Ssse3.Shuffle(v6, shuffleMask);
            var r7 = Ssse3.Shuffle(v7, shuffleMask);

            WriteSwapResult(dst + i, r0);
            WriteSwapResult(dst + i + BytesPerVector, r1);
            WriteSwapResult(dst + i + (2 * BytesPerVector), r2);
            WriteSwapResult(dst + i + (3 * BytesPerVector), r3);
            WriteSwapResult(dst + i + (4 * BytesPerVector), r4);
            WriteSwapResult(dst + i + (5 * BytesPerVector), r5);
            WriteSwapResult(dst + i + (6 * BytesPerVector), r6);
            WriteSwapResult(dst + i + (7 * BytesPerVector), r7);

            i += 8 * BytesPerVector;
        }

        // 4x unroll: 20 пикселей (60 байт), нужно +1 запас
        while (i + (4 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            WriteSwapResult(dst + i, r0);
            WriteSwapResult(dst + i + BytesPerVector, r1);
            WriteSwapResult(dst + i + (2 * BytesPerVector), r2);
            WriteSwapResult(dst + i + (3 * BytesPerVector), r3);

            i += 4 * BytesPerVector;
        }

        // 2x unroll: 10 пикселей (30 байт), нужно +1 запас
        while (i + (2 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);

            WriteSwapResult(dst + i, r0);
            WriteSwapResult(dst + i + BytesPerVector, r1);

            i += 2 * BytesPerVector;
        }

        // 1x Vector128: 5 пикселей (15 байт), нужно 16 для store
        while (i + BytesPerVector + 1 <= byteCount)
        {
            var v = Sse2.LoadVector128(src + i);
            var r = Ssse3.Shuffle(v, shuffleMask);
            WriteSwapResult(dst + i, r);
            i += BytesPerVector;
        }

        // Остаток scalar
        while (i + 3 <= byteCount)
        {
            var b = src[i];
            var g = src[i + 1];
            var r = src[i + 2];
            dst[i] = r;
            dst[i + 1] = g;
            dst[i + 2] = b;
            i += 3;
        }
    }

    /// <summary>Записывает 15 байт результата shuffle (через полный 16-byte store).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteSwapResult(byte* dst, Vector128<byte> v) =>
        Sse2.Store(dst, v);

    /// <summary>
    /// AVX2: Swap B↔R для 24-bit пикселей — идентичен SSE41 (8x unroll).
    /// VPSHUFB работает только in-lane, поэтому AVX2 не даёт преимущества для 24-bit shuffle.
    /// Реализация должна быть идентична SSE41 для паритета производительности.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRBAvx2(byte* src, byte* dst, int pixelCount)
    {
        var byteCount = pixelCount * 3;
        var i = 0;

        // Кешированная маска
        var shuffleMask = Bgr24Sse41Vectors.SwapRB24ShuffleMask;
        const int BytesPerVector = 15;

        // 8x unroll: 40 пикселей (120 байт) за итерацию
        while (i + (8 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));
            var v4 = Sse2.LoadVector128(src + i + (4 * BytesPerVector));
            var v5 = Sse2.LoadVector128(src + i + (5 * BytesPerVector));
            var v6 = Sse2.LoadVector128(src + i + (6 * BytesPerVector));
            var v7 = Sse2.LoadVector128(src + i + (7 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);
            var r4 = Ssse3.Shuffle(v4, shuffleMask);
            var r5 = Ssse3.Shuffle(v5, shuffleMask);
            var r6 = Ssse3.Shuffle(v6, shuffleMask);
            var r7 = Ssse3.Shuffle(v7, shuffleMask);

            // Overlapping 16-byte stores
            Sse2.Store(dst + i, r0);
            Sse2.Store(dst + i + BytesPerVector, r1);
            Sse2.Store(dst + i + (2 * BytesPerVector), r2);
            Sse2.Store(dst + i + (3 * BytesPerVector), r3);
            Sse2.Store(dst + i + (4 * BytesPerVector), r4);
            Sse2.Store(dst + i + (5 * BytesPerVector), r5);
            Sse2.Store(dst + i + (6 * BytesPerVector), r6);
            Sse2.Store(dst + i + (7 * BytesPerVector), r7);

            i += 8 * BytesPerVector;
        }

        // 4x unroll остаток
        while (i + (4 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            Sse2.Store(dst + i, r0);
            Sse2.Store(dst + i + BytesPerVector, r1);
            Sse2.Store(dst + i + (2 * BytesPerVector), r2);
            Sse2.Store(dst + i + (3 * BytesPerVector), r3);

            i += 4 * BytesPerVector;
        }

        // Одиночные итерации
        while (i + BytesPerVector + 1 <= byteCount)
        {
            var v = Sse2.LoadVector128(src + i);
            var r = Ssse3.Shuffle(v, shuffleMask);
            Sse2.Store(dst + i, r);
            i += BytesPerVector;
        }

        // Остаток scalar
        while (i + 3 <= byteCount)
        {
            var b = src[i];
            var g = src[i + 1];
            var r = src[i + 2];

            dst[i] = r;
            dst[i + 1] = g;
            dst[i + 2] = b;

            i += 3;
        }
    }

    /// <summary>
    /// SSSE3: 40 пикселей (120 байт) за итерацию (8x unroll).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRBSsse3(byte* src, byte* dst, int pixelCount)
    {
        var byteCount = pixelCount * 3;
        var i = 0;

        // Кешированная маска
        var shuffleMask = Bgr24Sse41Vectors.SwapRB24ShuffleMask;
        const int BytesPerVector = 15;

        // 8x unroll: 40 пикселей (120 байт) за итерацию
        while (i + (8 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));
            var v4 = Sse2.LoadVector128(src + i + (4 * BytesPerVector));
            var v5 = Sse2.LoadVector128(src + i + (5 * BytesPerVector));
            var v6 = Sse2.LoadVector128(src + i + (6 * BytesPerVector));
            var v7 = Sse2.LoadVector128(src + i + (7 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);
            var r4 = Ssse3.Shuffle(v4, shuffleMask);
            var r5 = Ssse3.Shuffle(v5, shuffleMask);
            var r6 = Ssse3.Shuffle(v6, shuffleMask);
            var r7 = Ssse3.Shuffle(v7, shuffleMask);

            // Overlapping 16-byte stores
            Sse2.Store(dst + i, r0);
            Sse2.Store(dst + i + BytesPerVector, r1);
            Sse2.Store(dst + i + (2 * BytesPerVector), r2);
            Sse2.Store(dst + i + (3 * BytesPerVector), r3);
            Sse2.Store(dst + i + (4 * BytesPerVector), r4);
            Sse2.Store(dst + i + (5 * BytesPerVector), r5);
            Sse2.Store(dst + i + (6 * BytesPerVector), r6);
            Sse2.Store(dst + i + (7 * BytesPerVector), r7);

            i += 8 * BytesPerVector;
        }

        // 4x unroll остаток
        while (i + (4 * BytesPerVector) + 1 <= byteCount)
        {
            var v0 = Sse2.LoadVector128(src + i);
            var v1 = Sse2.LoadVector128(src + i + BytesPerVector);
            var v2 = Sse2.LoadVector128(src + i + (2 * BytesPerVector));
            var v3 = Sse2.LoadVector128(src + i + (3 * BytesPerVector));

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            Sse2.Store(dst + i, r0);
            Sse2.Store(dst + i + BytesPerVector, r1);
            Sse2.Store(dst + i + (2 * BytesPerVector), r2);
            Sse2.Store(dst + i + (3 * BytesPerVector), r3);

            i += 4 * BytesPerVector;
        }

        // Одиночные итерации
        while (i + BytesPerVector + 1 <= byteCount)
        {
            var v = Sse2.LoadVector128(src + i);
            var r = Ssse3.Shuffle(v, shuffleMask);
            Sse2.Store(dst + i, r);
            i += BytesPerVector;
        }

        // Остаток scalar
        while (i + 3 <= byteCount)
        {
            var b = src[i];
            var g = src[i + 1];
            var r = src[i + 2];

            dst[i] = r;
            dst[i + 1] = g;
            dst[i + 2] = b;

            i += 3;
        }
    }

    /// <summary>Скалярная реализация для fallback с 8x loop unrolling.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRBScalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (24 байта → 24 байта, swap R↔B)
        while (pixelCount >= 8)
        {
            // Пиксель 0
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0];
            // Пиксель 1
            dst[3] = src[5]; dst[4] = src[4]; dst[5] = src[3];
            // Пиксель 2
            dst[6] = src[8]; dst[7] = src[7]; dst[8] = src[6];
            // Пиксель 3
            dst[9] = src[11]; dst[10] = src[10]; dst[11] = src[9];
            // Пиксель 4
            dst[12] = src[14]; dst[13] = src[13]; dst[14] = src[12];
            // Пиксель 5
            dst[15] = src[17]; dst[16] = src[16]; dst[17] = src[15];
            // Пиксель 6
            dst[18] = src[20]; dst[19] = src[19]; dst[20] = src[18];
            // Пиксель 7
            dst[21] = src[23]; dst[22] = src[22]; dst[23] = src[21];

            src += 24;
            dst += 24;
            pixelCount -= 8;
        }

        // Остаток
        while (pixelCount > 0)
        {
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0];
            src += 3;
            dst += 3;
            pixelCount--;
        }
    }

    #endregion
}
