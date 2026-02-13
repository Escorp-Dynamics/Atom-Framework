#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Rgb24.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Rgb24 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromRgb24(Rgb24 rgb)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y = ((19595 * rgb.R) + (38470 * rgb.G) + (7471 * rgb.B) + 32768) >> 16;
        return new((byte)y);
    }

    /// <summary>Конвертирует Gray8 в Rgb24 (R = G = B = Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24() => new(Value, Value, Value);

    #endregion

    #region Batch Conversion (Gray8 → Rgb24)

    /// <summary>Пакетная конвертация Gray8 → Rgb24.</summary>
    public static void ToRgb24(ReadOnlySpan<Gray8> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Rgb24 с явным указанием ускорителя.</summary>
    public static unsafe void ToRgb24(ReadOnlySpan<Gray8> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Rgb24* dstPtr = destination)
                ToRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToRgb24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToRgb24Core(ReadOnlySpan<Gray8> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToRgb24Sse41(source, destination);
                break;
            default:
                ToRgb24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToRgb24Parallel(Gray8* source, Rgb24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgb24Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Rgb24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Rgb24 → Gray8)

    /// <summary>Пакетная конвертация Rgb24 → Gray8.</summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Gray8> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgb24 → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgb24* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromRgb24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromRgb24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromRgb24Sse41(source, destination);
                break;
            default:
                FromRgb24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromRgb24Parallel(Rgb24* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgb24Core(new ReadOnlySpan<Rgb24>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Scalar(ReadOnlySpan<Gray8> source, Span<Rgb24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (8 байт Gray8 → 24 байта Rgb24)
            while (count >= 8)
            {
                var g0 = src[0]; var g1 = src[1]; var g2 = src[2]; var g3 = src[3];
                var g4 = src[4]; var g5 = src[5]; var g6 = src[6]; var g7 = src[7];

                dst[0] = g0; dst[1] = g0; dst[2] = g0;
                dst[3] = g1; dst[4] = g1; dst[5] = g1;
                dst[6] = g2; dst[7] = g2; dst[8] = g2;
                dst[9] = g3; dst[10] = g3; dst[11] = g3;
                dst[12] = g4; dst[13] = g4; dst[14] = g4;
                dst[15] = g5; dst[16] = g5; dst[17] = g5;
                dst[18] = g6; dst[19] = g6; dst[20] = g6;
                dst[21] = g7; dst[22] = g7; dst[23] = g7;

                src += 8;
                dst += 24;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                var g = *src++;
                *dst++ = g; *dst++ = g; *dst++ = g;
                count--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Scalar(ReadOnlySpan<Rgb24> source, Span<Gray8> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (24 байта Rgb24 → 8 байт Gray8)
            while (count >= 8)
            {
                dst[0] = (byte)(((GrayCoeffR * src[0]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[2]) + GrayHalf) >> 16);
                dst[1] = (byte)(((GrayCoeffR * src[3]) + (GrayCoeffG * src[4]) + (GrayCoeffB * src[5]) + GrayHalf) >> 16);
                dst[2] = (byte)(((GrayCoeffR * src[6]) + (GrayCoeffG * src[7]) + (GrayCoeffB * src[8]) + GrayHalf) >> 16);
                dst[3] = (byte)(((GrayCoeffR * src[9]) + (GrayCoeffG * src[10]) + (GrayCoeffB * src[11]) + GrayHalf) >> 16);
                dst[4] = (byte)(((GrayCoeffR * src[12]) + (GrayCoeffG * src[13]) + (GrayCoeffB * src[14]) + GrayHalf) >> 16);
                dst[5] = (byte)(((GrayCoeffR * src[15]) + (GrayCoeffG * src[16]) + (GrayCoeffB * src[17]) + GrayHalf) >> 16);
                dst[6] = (byte)(((GrayCoeffR * src[18]) + (GrayCoeffG * src[19]) + (GrayCoeffB * src[20]) + GrayHalf) >> 16);
                dst[7] = (byte)(((GrayCoeffR * src[21]) + (GrayCoeffG * src[22]) + (GrayCoeffB * src[23]) + GrayHalf) >> 16);

                src += 24;
                dst += 8;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                *dst++ = (byte)(((GrayCoeffR * src[0]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[2]) + GrayHalf) >> 16);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Rgb24)

    /// <summary>
    /// SSE41: Gray8 → Rgb24.
    /// Дублирует каждый байт в R, G, B.
    /// 16 пикселей за итерацию с оптимизированным shuffle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Sse41(ReadOnlySpan<Gray8> source, Span<Rgb24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Shuffle маски для 16 пикселей Gray → 48 байт RGB24
            // Маска 0: [Y0,Y0,Y0,Y1,Y1,Y1,Y2,Y2,Y2,Y3,Y3,Y3,Y4,Y4,Y4,Y5] = байты 0-15
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            // Маска 1: [Y5,Y5,Y6,Y6,Y6,Y7,Y7,Y7,Y8,Y8,Y8,Y9,Y9,Y9,Y10,Y10] = байты 16-31
            var shuffle1_lo = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Lo;
            var shuffle1_hi = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Hi;
            // Маска 2: [Y10,Y11,Y11,Y11,Y12,Y12,Y12,Y13,Y13,Y13,Y14,Y14,Y14,Y15,Y15,Y15] = байты 32-47
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_32_47;

            // === 16 пикселей за итерацию ===
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Байты 0-15: берём из младших 6 пикселей
                var rgb0 = Ssse3.Shuffle(gray, shuffle0);

                // Байты 16-31: нужны пиксели 5-10
                // Используем обе маски и объединяем
                var rgb1_lo = Ssse3.Shuffle(gray, shuffle1_lo);
                var rgb1_hi = Ssse3.Shuffle(gray, shuffle1_hi);
                var rgb1 = Sse2.Or(rgb1_lo, rgb1_hi);

                // Байты 32-47: берём из старших пикселей 10-15
                var rgb2 = Ssse3.Shuffle(gray, shuffle2);

                // Записываем 48 байт
                Sse2.Store(dst, rgb0);
                Sse2.Store(dst + 16, rgb1);
                Sse2.Store(dst + 32, rgb2);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // 8 пикселей fallback
            while (count >= 8)
            {
                var gray = Vector64.Load(src).ToVector128Unsafe();

                var rgb0 = Ssse3.Shuffle(gray, shuffle0);
                var rgb1 = Ssse3.Shuffle(gray, shuffle1_lo);

                Sse2.Store(dst, rgb0);
                Unsafe.WriteUnaligned(dst + 16, rgb1.AsUInt64().GetElement(0));

                src += 8;
                dst += 24;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = v;
                *dst++ = v;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Rgb24 → Gray8) — PMADDUBSW Pipeline

    /// <summary>
    /// SSE4.1: Rgb24 → Gray8 с использованием PMADDUBSW + PMADDWD.
    /// Y = (38×R + 75×G + 15×B) >> 7 ≈ 0.297R + 0.586G + 0.117B
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Sse41(ReadOnlySpan<Rgb24> source, Span<Gray8> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Shuffle маска: RGB24 (12 байт) → RGBA32 (16 байт) с нулевым padding
            var shuffleRgb24ToRgba32 = Gray8Sse41Vectors.ShuffleRgb24ToRgba32;
            // Q7 коэффициенты для PMADDUBSW
            var coeffs = Gray8Sse41Vectors.PmaddubswCoeffsRgba;
            // PMADDWD коэффициенты [1,1,...] для суммирования пар int16→int32
            var ones16 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32 для Q7
            var roundingBias32 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 16 пикселей за итерацию (48 байт RGB24 → 16 байт Gray8) ===
            while (count >= 16)
            {
                // Загружаем 48 байт RGB24 с overlapping reads (12 байт шаг)
                var rgb0 = Sse2.LoadVector128(src);
                var rgb1 = Sse2.LoadVector128(src + 12);
                var rgb2 = Sse2.LoadVector128(src + 24);
                var rgb3 = Sse2.LoadVector128(src + 36);

                // Shuffle: RGB24 → [R,G,B,0] × 4 = RGBA32 формат
                var rgba0 = Ssse3.Shuffle(rgb0, shuffleRgb24ToRgba32);
                var rgba1 = Ssse3.Shuffle(rgb1, shuffleRgb24ToRgba32);
                var rgba2 = Ssse3.Shuffle(rgb2, shuffleRgb24ToRgba32);
                var rgba3 = Ssse3.Shuffle(rgb3, shuffleRgb24ToRgba32);

                // PMADDUBSW: [R,G,B,0] × [38,75,15,0] → [R×38+G×75, B×15+0] × 4
                var prod0 = Ssse3.MultiplyAddAdjacent(rgba0, coeffs);
                var prod1 = Ssse3.MultiplyAddAdjacent(rgba1, coeffs);
                var prod2 = Ssse3.MultiplyAddAdjacent(rgba2, coeffs);
                var prod3 = Ssse3.MultiplyAddAdjacent(rgba3, coeffs);

                // PMADDWD: суммирование пар → int32
                var y0 = Sse2.MultiplyAddAdjacent(prod0, ones16);
                var y1 = Sse2.MultiplyAddAdjacent(prod1, ones16);
                var y2 = Sse2.MultiplyAddAdjacent(prod2, ones16);
                var y3 = Sse2.MultiplyAddAdjacent(prod3, ones16);

                // Rounding + shift (объединено для уменьшения latency)
                y0 = Sse2.ShiftRightArithmetic(Sse2.Add(y0, roundingBias32), 7);
                y1 = Sse2.ShiftRightArithmetic(Sse2.Add(y1, roundingBias32), 7);
                y2 = Sse2.ShiftRightArithmetic(Sse2.Add(y2, roundingBias32), 7);
                y3 = Sse2.ShiftRightArithmetic(Sse2.Add(y3, roundingBias32), 7);

                // Pack int32 → int16 → uint8
                var y01 = Sse2.PackSignedSaturate(y0, y1);
                var y23 = Sse2.PackSignedSaturate(y2, y3);
                var yBytes = Sse2.PackUnsignedSaturate(y01, y23);

                Sse2.Store(dst, yBytes);

                src += 48;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            while (count >= 4)
            {
                var rgb = Sse2.LoadVector128(src);
                var rgba = Ssse3.Shuffle(rgb, shuffleRgb24ToRgba32);
                var prod = Ssse3.MultiplyAddAdjacent(rgba, coeffs);
                var y = Sse2.MultiplyAddAdjacent(prod, ones16);
                y = Sse2.Add(y, roundingBias32);
                y = Sse2.ShiftRightArithmetic(y, 7);
                var yPacked = Sse2.PackSignedSaturate(y, y);
                var yBytes = Sse2.PackUnsignedSaturate(yPacked, yPacked);

                *(uint*)dst = yBytes.AsUInt32().GetElement(0);

                src += 12;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var r = src[0];
                var g = src[1];
                var b = src[2];
                // Q7: (38×R + 75×G + 15×B + 64) >> 7
                *dst++ = (byte)(((38 * r) + (75 * g) + (15 * b) + 64) >> 7);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Rgb24 → Gray8) — True VPMADDUBSW Pipeline

    /// <summary>
    /// AVX2: Rgb24 → Gray8 с использованием VPMADDUBSW + VPMADDWD.
    /// Y = (38×R + 75×G + 15×B) >> 7 ≈ 0.297R + 0.586G + 0.117B
    /// 32 пикселя за итерацию с настоящими AVX2 инструкциями для математики.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx2(ReadOnlySpan<Rgb24> source, Span<Gray8> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // SSE shuffle маска: RGB24 → RGBA32 формат (12 байт → 16 байт)
            var shuffleRgb24ToRgba32 = Gray8Sse41Vectors.ShuffleRgb24ToRgba32;

            // AVX2 коэффициенты для VPMADDUBSW
            var coeffs256 = Gray8Avx2Vectors.PmaddubswCoeffsRgba;
            // AVX2 VPMADDWD коэффициенты [1,1,...] для суммирования пар
            var ones16_256 = Gray8Avx2Vectors.Ones16;
            // AVX2 Rounding bias 64 в int32
            var roundingBias32_256 = Gray8Avx2Vectors.RoundingBias64_Int32;

            // SSE fallback константы
            var coeffs128 = Gray8Sse41Vectors.PmaddubswCoeffsRgba;
            var ones16_128 = Gray8Sse41Vectors.Ones16;
            var roundingBias32_128 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 32 пикселя за итерацию (96 байт RGB24 → 32 байт Gray8) ===
            while (count >= 32)
            {
                // Загружаем 96 байт RGB24 с overlapping SSE reads + shuffle
                var rgba0 = Ssse3.Shuffle(Sse2.LoadVector128(src), shuffleRgb24ToRgba32);
                var rgba1 = Ssse3.Shuffle(Sse2.LoadVector128(src + 12), shuffleRgb24ToRgba32);
                var rgba2 = Ssse3.Shuffle(Sse2.LoadVector128(src + 24), shuffleRgb24ToRgba32);
                var rgba3 = Ssse3.Shuffle(Sse2.LoadVector128(src + 36), shuffleRgb24ToRgba32);
                var rgba4 = Ssse3.Shuffle(Sse2.LoadVector128(src + 48), shuffleRgb24ToRgba32);
                var rgba5 = Ssse3.Shuffle(Sse2.LoadVector128(src + 60), shuffleRgb24ToRgba32);
                var rgba6 = Ssse3.Shuffle(Sse2.LoadVector128(src + 72), shuffleRgb24ToRgba32);
                var rgba7 = Ssse3.Shuffle(Sse2.LoadVector128(src + 84), shuffleRgb24ToRgba32);

                // Объединяем пары SSE → AVX2 (Avx.InsertVector128 быстрее Vector256.Create)
                var rgba01 = Avx.InsertVector128(rgba0.ToVector256Unsafe(), rgba1, 1);
                var rgba23 = Avx.InsertVector128(rgba2.ToVector256Unsafe(), rgba3, 1);
                var rgba45 = Avx.InsertVector128(rgba4.ToVector256Unsafe(), rgba5, 1);
                var rgba67 = Avx.InsertVector128(rgba6.ToVector256Unsafe(), rgba7, 1);

                // AVX2 VPMADDUBSW: [R,G,B,0] × [38,75,15,0] → [R×38+G×75, B×15+0]
                var prod01 = Avx2.MultiplyAddAdjacent(rgba01.AsByte(), coeffs256.AsSByte());
                var prod23 = Avx2.MultiplyAddAdjacent(rgba23.AsByte(), coeffs256.AsSByte());
                var prod45 = Avx2.MultiplyAddAdjacent(rgba45.AsByte(), coeffs256.AsSByte());
                var prod67 = Avx2.MultiplyAddAdjacent(rgba67.AsByte(), coeffs256.AsSByte());

                // AVX2 VPMADDWD: [rg,b0] × [1,1] → rg+b0 в int32
                var y01 = Avx2.MultiplyAddAdjacent(prod01, ones16_256);
                var y23 = Avx2.MultiplyAddAdjacent(prod23, ones16_256);
                var y45 = Avx2.MultiplyAddAdjacent(prod45, ones16_256);
                var y67 = Avx2.MultiplyAddAdjacent(prod67, ones16_256);

                // AVX2 VPADDD: rounding bias +64
                y01 = Avx2.Add(y01, roundingBias32_256);
                y23 = Avx2.Add(y23, roundingBias32_256);
                y45 = Avx2.Add(y45, roundingBias32_256);
                y67 = Avx2.Add(y67, roundingBias32_256);

                // AVX2 VPSRAD: shift >>7
                y01 = Avx2.ShiftRightArithmetic(y01, 7);
                y23 = Avx2.ShiftRightArithmetic(y23, 7);
                y45 = Avx2.ShiftRightArithmetic(y45, 7);
                y67 = Avx2.ShiftRightArithmetic(y67, 7);

                // AVX2 VPACKSSDW: int32 → int16 (in-lane, нужна перестановка)
                var y0123 = Avx2.PackSignedSaturate(y01, y23);
                var y4567 = Avx2.PackSignedSaturate(y45, y67);

                // Исправляем порядок после in-lane pack (int32 → int16)
                y0123 = Avx2.Permute4x64(y0123.AsInt64(), 0b11_01_10_00).AsInt16();
                y4567 = Avx2.Permute4x64(y4567.AsInt64(), 0b11_01_10_00).AsInt16();

                // AVX2 VPACKUSWB: int16 → uint8 (in-lane)
                var yBytesPacked = Avx2.PackUnsignedSaturate(y0123, y4567);

                // Исправляем порядок после второго pack
                var yBytes = Avx2.Permute4x64(yBytesPacked.AsInt64(), 0b11_01_10_00).AsByte();

                Avx.Store(dst, yBytes);

                src += 96;
                dst += 32;
                count -= 32;
            }

            // 16 пикселей fallback (SSE)
            while (count >= 16)
            {
                var rgb0 = Sse2.LoadVector128(src);
                var rgb1 = Sse2.LoadVector128(src + 12);
                var rgb2 = Sse2.LoadVector128(src + 24);
                var rgb3 = Sse2.LoadVector128(src + 36);

                var rgba0 = Ssse3.Shuffle(rgb0, shuffleRgb24ToRgba32);
                var rgba1 = Ssse3.Shuffle(rgb1, shuffleRgb24ToRgba32);
                var rgba2 = Ssse3.Shuffle(rgb2, shuffleRgb24ToRgba32);
                var rgba3 = Ssse3.Shuffle(rgb3, shuffleRgb24ToRgba32);

                var prod0 = Ssse3.MultiplyAddAdjacent(rgba0, coeffs128);
                var prod1 = Ssse3.MultiplyAddAdjacent(rgba1, coeffs128);
                var prod2 = Ssse3.MultiplyAddAdjacent(rgba2, coeffs128);
                var prod3 = Ssse3.MultiplyAddAdjacent(rgba3, coeffs128);

                var y0 = Sse2.MultiplyAddAdjacent(prod0, ones16_128);
                var y1 = Sse2.MultiplyAddAdjacent(prod1, ones16_128);
                var y2 = Sse2.MultiplyAddAdjacent(prod2, ones16_128);
                var y3 = Sse2.MultiplyAddAdjacent(prod3, ones16_128);

                y0 = Sse2.ShiftRightArithmetic(Sse2.Add(y0, roundingBias32_128), 7);
                y1 = Sse2.ShiftRightArithmetic(Sse2.Add(y1, roundingBias32_128), 7);
                y2 = Sse2.ShiftRightArithmetic(Sse2.Add(y2, roundingBias32_128), 7);
                y3 = Sse2.ShiftRightArithmetic(Sse2.Add(y3, roundingBias32_128), 7);

                var y01 = Sse2.PackSignedSaturate(y0, y1);
                var y23 = Sse2.PackSignedSaturate(y2, y3);
                var yBytes = Sse2.PackUnsignedSaturate(y01, y23);

                Sse2.Store(dst, yBytes);

                src += 48;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            while (count >= 4)
            {
                var rgb = Sse2.LoadVector128(src);
                var rgba = Ssse3.Shuffle(rgb, shuffleRgb24ToRgba32);
                var prod = Ssse3.MultiplyAddAdjacent(rgba, coeffs128);
                var y = Sse2.MultiplyAddAdjacent(prod, ones16_128);
                y = Sse2.Add(y, roundingBias32_128);
                y = Sse2.ShiftRightArithmetic(y, 7);
                var yPacked = Sse2.PackSignedSaturate(y, y);
                var yBytes = Sse2.PackUnsignedSaturate(yPacked, yPacked);

                *(uint*)dst = yBytes.AsUInt32().GetElement(0);

                src += 12;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var r = src[0];
                var g = src[1];
                var b = src[2];
                *dst++ = (byte)(((38 * r) + (75 * g) + (15 * b) + 64) >> 7);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Rgb24)

    /// <summary>
    /// AVX2: Gray8 → Rgb24.
    /// 32 пикселя за итерацию с настоящим AVX2 + SSE для записи 96 байт.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx2(ReadOnlySpan<Gray8> source, Span<Rgb24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // SSE маски для shuffle
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1_lo = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Lo;
            var shuffle1_hi = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Hi;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_32_47;

            // === 32 пикселя за итерацию (32 байт → 96 байт) ===
            while (count >= 32)
            {
                // Загружаем 32 Gray8 пикселей с AVX2
                var gray = Avx.LoadVector256(src);

                // Обрабатываем 2 группы по 16 пикселей (SSE shuffle работает in-lane)
                var gray_lo = gray.GetLower();  // пиксели 0-15
                var gray_hi = gray.GetUpper();  // пиксели 16-31

                // === Первые 16 пикселей → 48 байт ===
                var rgb0_0 = Ssse3.Shuffle(gray_lo, shuffle0);
                var rgb0_1_lo = Ssse3.Shuffle(gray_lo, shuffle1_lo);
                var rgb0_1_hi = Ssse3.Shuffle(gray_lo, shuffle1_hi);
                var rgb0_1 = Sse2.Or(rgb0_1_lo, rgb0_1_hi);
                var rgb0_2 = Ssse3.Shuffle(gray_lo, shuffle2);

                // === Вторые 16 пикселей → 48 байт ===
                var rgb1_0 = Ssse3.Shuffle(gray_hi, shuffle0);
                var rgb1_1_lo = Ssse3.Shuffle(gray_hi, shuffle1_lo);
                var rgb1_1_hi = Ssse3.Shuffle(gray_hi, shuffle1_hi);
                var rgb1_1 = Sse2.Or(rgb1_1_lo, rgb1_1_hi);
                var rgb1_2 = Ssse3.Shuffle(gray_hi, shuffle2);

                // Записываем 96 байт (6 × 16)
                Sse2.Store(dst, rgb0_0);
                Sse2.Store(dst + 16, rgb0_1);
                Sse2.Store(dst + 32, rgb0_2);
                Sse2.Store(dst + 48, rgb1_0);
                Sse2.Store(dst + 64, rgb1_1);
                Sse2.Store(dst + 80, rgb1_2);

                src += 32;
                dst += 96;
                count -= 32;
            }

            // 16 пикселей (SSE fallback)
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                var rgb0 = Ssse3.Shuffle(gray, shuffle0);
                var rgb1_lo = Ssse3.Shuffle(gray, shuffle1_lo);
                var rgb1_hi = Ssse3.Shuffle(gray, shuffle1_hi);
                var rgb1 = Sse2.Or(rgb1_lo, rgb1_hi);
                var rgb2 = Ssse3.Shuffle(gray, shuffle2);

                Sse2.Store(dst, rgb0);
                Sse2.Store(dst + 16, rgb1);
                Sse2.Store(dst + 32, rgb2);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // 8 пикселей fallback
            while (count >= 8)
            {
                var gray = Vector64.Load(src).ToVector128Unsafe();

                var rgb0 = Ssse3.Shuffle(gray, shuffle0);
                var rgb1 = Ssse3.Shuffle(gray, shuffle1_lo);

                Sse2.Store(dst, rgb0);
                Unsafe.WriteUnaligned(dst + 16, rgb1.AsUInt64().GetElement(0));

                src += 8;
                dst += 24;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = v;
                *dst++ = v;
                count--;
            }
        }
    }

    #endregion

    #region Deinterleave Helpers (RGB24)

    /// <summary>Деинтерливинг 8 RGB24 пикселей (24 байта) → R, G, B (по 8 байт каждый).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb8(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        var bytes0 = Sse2.LoadVector128(src);
        var bytes1 = Vector64.Load(src + 16).ToVector128Unsafe();

        r = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleR0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleR1));
        g = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleG0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleG1));
        b = Sse2.Or(Ssse3.Shuffle(bytes0, YCbCrSse41Vectors.ShuffleB0),
                    Ssse3.Shuffle(bytes1, YCbCrSse41Vectors.ShuffleB1));
    }

    /// <summary>Деинтерливинг 16 RGB24 пикселей (48 байт) → R, G, B (по 16 байт каждый).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void DeinterleaveRgb16(byte* src, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        DeinterleaveRgb8(src, out var r0, out var g0, out var b0);
        DeinterleaveRgb8(src + 24, out var r1, out var g1, out var b1);

        // Используем UnpackLow для объединения результатов
        r = Sse2.UnpackLow(r0.AsUInt64(), r1.AsUInt64()).AsByte();
        g = Sse2.UnpackLow(g0.AsUInt64(), g1.AsUInt64()).AsByte();
        b = Sse2.UnpackLow(b0.AsUInt64(), b1.AsUInt64()).AsByte();
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Rgb24 → Gray8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray8(Rgb24 rgb) => FromRgb24(rgb);

    /// <summary>Явное преобразование Gray8 → Rgb24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgb24(Gray8 gray) => gray.ToRgb24();

    #endregion
}
