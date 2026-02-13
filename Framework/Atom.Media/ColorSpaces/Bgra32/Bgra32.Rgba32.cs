#pragma warning disable CA1000, CA2208, MA0051, S4136

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Rgba32.
/// SIMD: swap B и R каналов (альфа остаётся на месте).
/// </summary>
public readonly partial struct Bgra32
{
    /// <summary>
    /// Реализованные ускорители для конвертации Bgra32 ↔ Rgba32.
    /// </summary>
    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в Bgra32 (swap R и B).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromRgba32(Rgba32 rgba) => new(rgba.B, rgba.G, rgba.R, rgba.A);

    /// <summary>Конвертирует Bgra32 в Rgba32 (swap B и R).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32() => new(R, G, B, A);

    #endregion

    #region Batch Conversion (Bgra32 ↔ Rgba32)

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgra32 с SIMD.
    /// </summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Bgra32> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgra32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Rgba32.</param>
    /// <param name="destination">Целевой буфер Bgra32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
            {
                FromRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgba32 с SIMD.
    /// </summary>
    public static void ToRgba32(ReadOnlySpan<Bgra32> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgba32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgra32.</param>
    /// <param name="destination">Целевой буфер Rgba32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static unsafe void ToRgba32(ReadOnlySpan<Bgra32> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
            {
                ToRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core SIMD (Rgba32)

    /// <summary>Однопоточная SIMD конвертация Rgba32 → Bgra32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    SwapRB32Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    SwapRB32Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    SwapRB32Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    SwapRB32Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Однопоточная SIMD конвертация Bgra32 → Rgba32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Core(ReadOnlySpan<Bgra32> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            // BGRA → RGBA и RGBA → BGRA — идентичная операция (swap первого и третьего байта)
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    SwapRB32Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    SwapRB32Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    SwapRB32Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    SwapRB32Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    #endregion

    #region Parallel (Rgba32)

    /// <summary>Параллельная конвертация Rgba32 → Bgra32 с выбранным ускорителем.</summary>
    private static unsafe void FromRgba32Parallel(Rgba32* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgba32Core(new ReadOnlySpan<Rgba32>(source + start, size), new Span<Bgra32>(destination + start, size), selected);
        });
    }

    /// <summary>Параллельная конвертация Bgra32 → Rgba32 с выбранным ускорителем.</summary>
    private static unsafe void ToRgba32Parallel(Bgra32* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgba32Core(new ReadOnlySpan<Bgra32>(source + start, size), new Span<Rgba32>(destination + start, size), selected);
        });
    }

    #endregion

    #region SSSE3 Implementation (Rgba32)

    /// <summary>
    /// SSSE3: Swap R и B в 32-битных пикселях (RGBA ↔ BGRA).
    /// 32 пикселя за итерацию (8x unroll).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRB32Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var mask = Bgra32Sse41Vectors.SwapRB32Mask;

        // 8x unroll: 32 пикселя = 128 байт за итерацию
        while (i + 32 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 4));
            var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
            var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
            var v3 = Sse2.LoadVector128(src + (i * 4) + 48);
            var v4 = Sse2.LoadVector128(src + (i * 4) + 64);
            var v5 = Sse2.LoadVector128(src + (i * 4) + 80);
            var v6 = Sse2.LoadVector128(src + (i * 4) + 96);
            var v7 = Sse2.LoadVector128(src + (i * 4) + 112);

            var r0 = Ssse3.Shuffle(v0, mask);
            var r1 = Ssse3.Shuffle(v1, mask);
            var r2 = Ssse3.Shuffle(v2, mask);
            var r3 = Ssse3.Shuffle(v3, mask);
            var r4 = Ssse3.Shuffle(v4, mask);
            var r5 = Ssse3.Shuffle(v5, mask);
            var r6 = Ssse3.Shuffle(v6, mask);
            var r7 = Ssse3.Shuffle(v7, mask);

            Sse2.Store(dst + (i * 4), r0);
            Sse2.Store(dst + (i * 4) + 16, r1);
            Sse2.Store(dst + (i * 4) + 32, r2);
            Sse2.Store(dst + (i * 4) + 48, r3);
            Sse2.Store(dst + (i * 4) + 64, r4);
            Sse2.Store(dst + (i * 4) + 80, r5);
            Sse2.Store(dst + (i * 4) + 96, r6);
            Sse2.Store(dst + (i * 4) + 112, r7);

            i += 32;
        }

        // 4x unroll: 16 пикселей = 64 байта
        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 4));
            var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
            var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
            var v3 = Sse2.LoadVector128(src + (i * 4) + 48);

            var r0 = Ssse3.Shuffle(v0, mask);
            var r1 = Ssse3.Shuffle(v1, mask);
            var r2 = Ssse3.Shuffle(v2, mask);
            var r3 = Ssse3.Shuffle(v3, mask);

            Sse2.Store(dst + (i * 4), r0);
            Sse2.Store(dst + (i * 4) + 16, r1);
            Sse2.Store(dst + (i * 4) + 32, r2);
            Sse2.Store(dst + (i * 4) + 48, r3);

            i += 16;
        }

        // 4 пикселя = 16 байт
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, mask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[offset] = src[offset + 2];     // B ↔ R
            dst[offset + 1] = src[offset + 1]; // G
            dst[offset + 2] = src[offset];     // R ↔ B
            dst[offset + 3] = src[offset + 3]; // A
            i++;
        }
    }

    #endregion

    #region AVX2 Implementation (Rgba32)

    /// <summary>
    /// AVX2: Swap R и B в 32-битных пикселях (RGBA ↔ BGRA).
    /// 128 пикселей за итерацию (16x unroll для максимального ILP).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRB32Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var mask = Bgra32Avx2Vectors.SwapRB32Mask;

        // 16x unroll: 128 пикселей = 512 байт за итерацию
        while (i + 128 <= pixelCount)
        {
            // Первая половина (64 пикселя)
            var v0 = Avx.LoadVector256(src + (i * 4));
            var v1 = Avx.LoadVector256(src + (i * 4) + 32);
            var v2 = Avx.LoadVector256(src + (i * 4) + 64);
            var v3 = Avx.LoadVector256(src + (i * 4) + 96);
            var v4 = Avx.LoadVector256(src + (i * 4) + 128);
            var v5 = Avx.LoadVector256(src + (i * 4) + 160);
            var v6 = Avx.LoadVector256(src + (i * 4) + 192);
            var v7 = Avx.LoadVector256(src + (i * 4) + 224);

            var r0 = Avx2.Shuffle(v0, mask);
            var r1 = Avx2.Shuffle(v1, mask);
            var r2 = Avx2.Shuffle(v2, mask);
            var r3 = Avx2.Shuffle(v3, mask);
            var r4 = Avx2.Shuffle(v4, mask);
            var r5 = Avx2.Shuffle(v5, mask);
            var r6 = Avx2.Shuffle(v6, mask);
            var r7 = Avx2.Shuffle(v7, mask);

            Avx.Store(dst + (i * 4), r0);
            Avx.Store(dst + (i * 4) + 32, r1);
            Avx.Store(dst + (i * 4) + 64, r2);
            Avx.Store(dst + (i * 4) + 96, r3);
            Avx.Store(dst + (i * 4) + 128, r4);
            Avx.Store(dst + (i * 4) + 160, r5);
            Avx.Store(dst + (i * 4) + 192, r6);
            Avx.Store(dst + (i * 4) + 224, r7);

            // Вторая половина (64 пикселя, offset 256 байт)
            v0 = Avx.LoadVector256(src + (i * 4) + 256);
            v1 = Avx.LoadVector256(src + (i * 4) + 288);
            v2 = Avx.LoadVector256(src + (i * 4) + 320);
            v3 = Avx.LoadVector256(src + (i * 4) + 352);
            v4 = Avx.LoadVector256(src + (i * 4) + 384);
            v5 = Avx.LoadVector256(src + (i * 4) + 416);
            v6 = Avx.LoadVector256(src + (i * 4) + 448);
            v7 = Avx.LoadVector256(src + (i * 4) + 480);

            r0 = Avx2.Shuffle(v0, mask);
            r1 = Avx2.Shuffle(v1, mask);
            r2 = Avx2.Shuffle(v2, mask);
            r3 = Avx2.Shuffle(v3, mask);
            r4 = Avx2.Shuffle(v4, mask);
            r5 = Avx2.Shuffle(v5, mask);
            r6 = Avx2.Shuffle(v6, mask);
            r7 = Avx2.Shuffle(v7, mask);

            Avx.Store(dst + (i * 4) + 256, r0);
            Avx.Store(dst + (i * 4) + 288, r1);
            Avx.Store(dst + (i * 4) + 320, r2);
            Avx.Store(dst + (i * 4) + 352, r3);
            Avx.Store(dst + (i * 4) + 384, r4);
            Avx.Store(dst + (i * 4) + 416, r5);
            Avx.Store(dst + (i * 4) + 448, r6);
            Avx.Store(dst + (i * 4) + 480, r7);

            i += 128;
        }

        // 8x unroll fallback: 64 пикселя = 256 байт
        while (i + 64 <= pixelCount)
        {
            var v0 = Avx.LoadVector256(src + (i * 4));
            var v1 = Avx.LoadVector256(src + (i * 4) + 32);
            var v2 = Avx.LoadVector256(src + (i * 4) + 64);
            var v3 = Avx.LoadVector256(src + (i * 4) + 96);
            var v4 = Avx.LoadVector256(src + (i * 4) + 128);
            var v5 = Avx.LoadVector256(src + (i * 4) + 160);
            var v6 = Avx.LoadVector256(src + (i * 4) + 192);
            var v7 = Avx.LoadVector256(src + (i * 4) + 224);

            var r0 = Avx2.Shuffle(v0, mask);
            var r1 = Avx2.Shuffle(v1, mask);
            var r2 = Avx2.Shuffle(v2, mask);
            var r3 = Avx2.Shuffle(v3, mask);
            var r4 = Avx2.Shuffle(v4, mask);
            var r5 = Avx2.Shuffle(v5, mask);
            var r6 = Avx2.Shuffle(v6, mask);
            var r7 = Avx2.Shuffle(v7, mask);

            Avx.Store(dst + (i * 4), r0);
            Avx.Store(dst + (i * 4) + 32, r1);
            Avx.Store(dst + (i * 4) + 64, r2);
            Avx.Store(dst + (i * 4) + 96, r3);
            Avx.Store(dst + (i * 4) + 128, r4);
            Avx.Store(dst + (i * 4) + 160, r5);
            Avx.Store(dst + (i * 4) + 192, r6);
            Avx.Store(dst + (i * 4) + 224, r7);

            i += 64;
        }

        // 4x unroll: 32 пикселя = 128 байт
        while (i + 32 <= pixelCount)
        {
            var v0 = Avx.LoadVector256(src + (i * 4));
            var v1 = Avx.LoadVector256(src + (i * 4) + 32);
            var v2 = Avx.LoadVector256(src + (i * 4) + 64);
            var v3 = Avx.LoadVector256(src + (i * 4) + 96);

            var r0 = Avx2.Shuffle(v0, mask);
            var r1 = Avx2.Shuffle(v1, mask);
            var r2 = Avx2.Shuffle(v2, mask);
            var r3 = Avx2.Shuffle(v3, mask);

            Avx.Store(dst + (i * 4), r0);
            Avx.Store(dst + (i * 4) + 32, r1);
            Avx.Store(dst + (i * 4) + 64, r2);
            Avx.Store(dst + (i * 4) + 96, r3);

            i += 32;
        }

        // 8 пикселей = 32 байта
        while (i + 8 <= pixelCount)
        {
            var v = Avx.LoadVector256(src + (i * 4));
            var r = Avx2.Shuffle(v, mask);
            Avx.Store(dst + (i * 4), r);
            i += 8;
        }

        // Остаток SSE
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, Bgra32Sse41Vectors.SwapRB32Mask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[offset] = src[offset + 2];
            dst[offset + 1] = src[offset + 1];
            dst[offset + 2] = src[offset];
            dst[offset + 3] = src[offset + 3];
            i++;
        }
    }

    #endregion

    #region AVX512 Implementation (Rgba32)

    /// <summary>
    /// AVX512BW: Swap R и B в 32-битных пикселях (RGBA ↔ BGRA).
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRB32Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        // 16 пикселей = 64 байта
        while (i + 16 <= pixelCount)
        {
            var v = Avx512BW.LoadVector512(src + (i * 4));
            var r = Avx512BW.Shuffle(v, Bgra32Avx512Vectors.SwapRB32Mask);
            Avx512BW.Store(dst + (i * 4), r);
            i += 16;
        }

        // Остаток AVX2
        while (i + 8 <= pixelCount)
        {
            var v = Avx.LoadVector256(src + (i * 4));
            var r = Avx2.Shuffle(v, Bgra32Avx2Vectors.SwapRB32Mask);
            Avx.Store(dst + (i * 4), r);
            i += 8;
        }

        // Остаток SSE
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, Bgra32Sse41Vectors.SwapRB32Mask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[offset] = src[offset + 2];
            dst[offset + 1] = src[offset + 1];
            dst[offset + 2] = src[offset];
            dst[offset + 3] = src[offset + 3];
            i++;
        }
    }

    #endregion

    #region Scalar Implementation (Rgba32)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SwapRB32Scalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (32 байта → 32 байта, swap R↔B)
        while (pixelCount >= 8)
        {
            // Пиксель 0
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; dst[3] = src[3];
            // Пиксель 1
            dst[4] = src[6]; dst[5] = src[5]; dst[6] = src[4]; dst[7] = src[7];
            // Пиксель 2
            dst[8] = src[10]; dst[9] = src[9]; dst[10] = src[8]; dst[11] = src[11];
            // Пиксель 3
            dst[12] = src[14]; dst[13] = src[13]; dst[14] = src[12]; dst[15] = src[15];
            // Пиксель 4
            dst[16] = src[18]; dst[17] = src[17]; dst[18] = src[16]; dst[19] = src[19];
            // Пиксель 5
            dst[20] = src[22]; dst[21] = src[21]; dst[22] = src[20]; dst[23] = src[23];
            // Пиксель 6
            dst[24] = src[26]; dst[25] = src[25]; dst[26] = src[24]; dst[27] = src[27];
            // Пиксель 7
            dst[28] = src[30]; dst[29] = src[29]; dst[30] = src[28]; dst[31] = src[31];

            src += 32;
            dst += 32;
            pixelCount -= 8;
        }

        // Остаток
        while (pixelCount > 0)
        {
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; dst[3] = src[3];
            src += 4;
            dst += 4;
            pixelCount--;
        }
    }

    #endregion
}
