#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Bgr24.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Bgr24 в Gray8 (ITU-R BT.601).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromBgr24(Bgr24 bgr)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y = ((19595 * bgr.R) + (38470 * bgr.G) + (7471 * bgr.B) + 32768) >> 16;
        return new((byte)y);
    }

    /// <summary>Конвертирует Gray8 в Bgr24 (B = G = R = Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24() => new(Value, Value, Value);

    #endregion

    #region Batch Conversion (Gray8 → Bgr24)

    /// <summary>Пакетная конвертация Gray8 → Bgr24.</summary>
    public static void ToBgr24(ReadOnlySpan<Gray8> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Bgr24 с явным указанием ускорителя.</summary>
    public static unsafe void ToBgr24(ReadOnlySpan<Gray8> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
                ToBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToBgr24Core(ReadOnlySpan<Gray8> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToBgr24Sse41(source, destination);
                break;
            default:
                ToBgr24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToBgr24Parallel(Gray8* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgr24Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Bgr24>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Bgr24 → Gray8)

    /// <summary>Пакетная конвертация Bgr24 → Gray8.</summary>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Gray8> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromBgr24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromBgr24Sse41(source, destination);
                break;
            default:
                FromBgr24Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromBgr24Parallel(Bgr24* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgr24Core(new ReadOnlySpan<Bgr24>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Scalar(ReadOnlySpan<Gray8> source, Span<Bgr24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (8 байт Gray8 → 24 байта Bgr24)
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
    private static unsafe void FromBgr24Scalar(ReadOnlySpan<Bgr24> source, Span<Gray8> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (24 байта Bgr24 → 8 байт Gray8)
            // BGR порядок: B=src[0], G=src[1], R=src[2]
            while (count >= 8)
            {
                dst[0] = (byte)(((GrayCoeffR * src[2]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[0]) + GrayHalf) >> 16);
                dst[1] = (byte)(((GrayCoeffR * src[5]) + (GrayCoeffG * src[4]) + (GrayCoeffB * src[3]) + GrayHalf) >> 16);
                dst[2] = (byte)(((GrayCoeffR * src[8]) + (GrayCoeffG * src[7]) + (GrayCoeffB * src[6]) + GrayHalf) >> 16);
                dst[3] = (byte)(((GrayCoeffR * src[11]) + (GrayCoeffG * src[10]) + (GrayCoeffB * src[9]) + GrayHalf) >> 16);
                dst[4] = (byte)(((GrayCoeffR * src[14]) + (GrayCoeffG * src[13]) + (GrayCoeffB * src[12]) + GrayHalf) >> 16);
                dst[5] = (byte)(((GrayCoeffR * src[17]) + (GrayCoeffG * src[16]) + (GrayCoeffB * src[15]) + GrayHalf) >> 16);
                dst[6] = (byte)(((GrayCoeffR * src[20]) + (GrayCoeffG * src[19]) + (GrayCoeffB * src[18]) + GrayHalf) >> 16);
                dst[7] = (byte)(((GrayCoeffR * src[23]) + (GrayCoeffG * src[22]) + (GrayCoeffB * src[21]) + GrayHalf) >> 16);

                src += 24;
                dst += 8;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                *dst++ = (byte)(((GrayCoeffR * src[2]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[0]) + GrayHalf) >> 16);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Bgr24)

    /// <summary>
    /// SSE41: Gray8 → Bgr24.
    /// Идентично Gray8 → Rgb24 (порядок не важен для grayscale).
    /// 16 пикселей за итерацию с 3 shuffle масками (без ShiftRightLogical128BitLane).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Sse41(ReadOnlySpan<Gray8> source, Span<Bgr24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Новые маски без ShiftRightLogical128BitLane
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1_lo = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Lo;
            var shuffle1_hi = Gray8Sse41Vectors.ShuffleGrayToRgb24_16_31_Hi;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_32_47;

            // 16 пикселей = 16 байт входа → 48 байт выхода
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // 3 shuffle + 1 OR для 48 байт (без shift)
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
                Sse2.StoreLow((double*)(dst + 16), rgb1.AsDouble());

                src += 8;
                dst += 24;
                count -= 8;
            }

            // Остаток скаляром
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

    #region SSE41 Implementation (Bgr24 → Gray8) — PMADDUBSW Pipeline

    /// <summary>
    /// SSE41: Bgr24 → Gray8 с использованием PMADDUBSW + PMADDWD.
    /// Y = (15×B + 75×G + 38×R) >> 7 ≈ 0.117B + 0.586G + 0.297R
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Sse41(ReadOnlySpan<Bgr24> source, Span<Gray8> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Shuffle маска для BGR24 → BGRA32 (та же что RGB24, просто каналы называются B,G,R)
            var shuffleBgr24ToBgra32 = Gray8Sse41Vectors.ShuffleRgb24ToRgba32;
            // Q7 коэффициенты для PMADDUBSW [B×15 + G×75, R×38 + 0]
            var coeffs = Gray8Sse41Vectors.PmaddubswCoeffsBgra;
            // PMADDWD коэффициенты [1,1,...] для суммирования пар
            var ones16 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32
            var roundingBias32 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 16 пикселей за итерацию (48 байт BGR24 → 16 байт Gray8) ===
            while (count >= 16)
            {
                // Загружаем 48 байт BGR24 (16 пикселей)
                // Обрабатываем по 4 пикселя (12 байт → 16 байт BGRA) × 4 раза
                var bgr0 = Sse2.LoadVector128(src);       // пиксели 0-3 (+ extra)
                var bgr1 = Sse2.LoadVector128(src + 12);  // пиксели 4-7
                var bgr2 = Sse2.LoadVector128(src + 24);  // пиксели 8-11
                var bgr3 = Sse2.LoadVector128(src + 36);  // пиксели 12-15

                // Shuffle: BGR24 → [B,G,R,0] × 4 = BGRA32 формат
                var bgra0 = Ssse3.Shuffle(bgr0, shuffleBgr24ToBgra32);
                var bgra1 = Ssse3.Shuffle(bgr1, shuffleBgr24ToBgra32);
                var bgra2 = Ssse3.Shuffle(bgr2, shuffleBgr24ToBgra32);
                var bgra3 = Ssse3.Shuffle(bgr3, shuffleBgr24ToBgra32);

                // PMADDUBSW: [B,G,R,0] × [15,75,38,0] → [B×15+G×75, R×38+0] × 4 = 8 int16
                var prod0 = Ssse3.MultiplyAddAdjacent(bgra0, coeffs);
                var prod1 = Ssse3.MultiplyAddAdjacent(bgra1, coeffs);
                var prod2 = Ssse3.MultiplyAddAdjacent(bgra2, coeffs);
                var prod3 = Ssse3.MultiplyAddAdjacent(bgra3, coeffs);

                // PMADDWD: [bg,ra] × [1,1] → bg+ra в int32 (4 пикселя на вектор)
                var y0 = Sse2.MultiplyAddAdjacent(prod0, ones16);
                var y1 = Sse2.MultiplyAddAdjacent(prod1, ones16);
                var y2 = Sse2.MultiplyAddAdjacent(prod2, ones16);
                var y3 = Sse2.MultiplyAddAdjacent(prod3, ones16);

                // Добавляем rounding bias +64
                y0 = Sse2.Add(y0, roundingBias32);
                y1 = Sse2.Add(y1, roundingBias32);
                y2 = Sse2.Add(y2, roundingBias32);
                y3 = Sse2.Add(y3, roundingBias32);

                // Shift >>7
                y0 = Sse2.ShiftRightArithmetic(y0, 7);
                y1 = Sse2.ShiftRightArithmetic(y1, 7);
                y2 = Sse2.ShiftRightArithmetic(y2, 7);
                y3 = Sse2.ShiftRightArithmetic(y3, 7);

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
                var bgr = Sse2.LoadVector128(src);
                var bgra = Ssse3.Shuffle(bgr, shuffleBgr24ToBgra32);
                var prod = Ssse3.MultiplyAddAdjacent(bgra, coeffs);
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
                var b = src[0];
                var g = src[1];
                var r = src[2];
                *dst++ = (byte)(((15 * b) + (75 * g) + (38 * r) + 64) >> 7);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Bgr24 → Gray8) — True VPMADDUBSW Pipeline

    /// <summary>
    /// AVX2: Bgr24 → Gray8 с использованием VPMADDUBSW + VPMADDWD.
    /// Y = (15×B + 75×G + 38×R) >> 7 ≈ 0.117B + 0.586G + 0.297R
    /// 32 пикселя за итерацию с настоящими AVX2 инструкциями для математики.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Avx2(ReadOnlySpan<Bgr24> source, Span<Gray8> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // SSE shuffle маска: BGR24 → BGRA32 формат
            var shuffleBgr24ToBgra32 = Gray8Sse41Vectors.ShuffleRgb24ToRgba32;

            // AVX2 коэффициенты для VPMADDUBSW
            var coeffs256 = Gray8Avx2Vectors.PmaddubswCoeffsBgra;
            // AVX2 VPMADDWD коэффициенты [1,1,...] для суммирования пар
            var ones16_256 = Gray8Avx2Vectors.Ones16;
            // AVX2 Rounding bias 64 в int32
            var roundingBias32_256 = Gray8Avx2Vectors.RoundingBias64_Int32;

            // SSE fallback константы
            var coeffs128 = Gray8Sse41Vectors.PmaddubswCoeffsBgra;
            var ones16_128 = Gray8Sse41Vectors.Ones16;
            var roundingBias32_128 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 32 пикселя за итерацию (96 байт BGR24 → 32 байт Gray8) ===
            while (count >= 32)
            {
                // Загружаем 96 байт BGR24 (32 пикселя) с overlapping SSE reads
                // Shuffle BGR24 → BGRA32 (SSE, потому что in-lane)
                var bgra0 = Ssse3.Shuffle(Sse2.LoadVector128(src), shuffleBgr24ToBgra32);
                var bgra1 = Ssse3.Shuffle(Sse2.LoadVector128(src + 12), shuffleBgr24ToBgra32);
                var bgra2 = Ssse3.Shuffle(Sse2.LoadVector128(src + 24), shuffleBgr24ToBgra32);
                var bgra3 = Ssse3.Shuffle(Sse2.LoadVector128(src + 36), shuffleBgr24ToBgra32);
                var bgra4 = Ssse3.Shuffle(Sse2.LoadVector128(src + 48), shuffleBgr24ToBgra32);
                var bgra5 = Ssse3.Shuffle(Sse2.LoadVector128(src + 60), shuffleBgr24ToBgra32);
                var bgra6 = Ssse3.Shuffle(Sse2.LoadVector128(src + 72), shuffleBgr24ToBgra32);
                var bgra7 = Ssse3.Shuffle(Sse2.LoadVector128(src + 84), shuffleBgr24ToBgra32);

                // Объединяем пары SSE → AVX2 (Avx.InsertVector128 быстрее Vector256.Create)
                var bgra01 = Avx.InsertVector128(bgra0.ToVector256Unsafe(), bgra1, 1);
                var bgra23 = Avx.InsertVector128(bgra2.ToVector256Unsafe(), bgra3, 1);
                var bgra45 = Avx.InsertVector128(bgra4.ToVector256Unsafe(), bgra5, 1);
                var bgra67 = Avx.InsertVector128(bgra6.ToVector256Unsafe(), bgra7, 1);

                // AVX2 VPMADDUBSW: [B,G,R,0] × [15,75,38,0] → [B×15+G×75, R×38+0]
                var prod01 = Avx2.MultiplyAddAdjacent(bgra01.AsByte(), coeffs256.AsSByte());
                var prod23 = Avx2.MultiplyAddAdjacent(bgra23.AsByte(), coeffs256.AsSByte());
                var prod45 = Avx2.MultiplyAddAdjacent(bgra45.AsByte(), coeffs256.AsSByte());
                var prod67 = Avx2.MultiplyAddAdjacent(bgra67.AsByte(), coeffs256.AsSByte());

                // AVX2 VPMADDWD: [bg,ra] × [1,1] → bg+ra в int32
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
                var bgr0 = Sse2.LoadVector128(src);
                var bgr1 = Sse2.LoadVector128(src + 12);
                var bgr2 = Sse2.LoadVector128(src + 24);
                var bgr3 = Sse2.LoadVector128(src + 36);

                var bgra0 = Ssse3.Shuffle(bgr0, shuffleBgr24ToBgra32);
                var bgra1 = Ssse3.Shuffle(bgr1, shuffleBgr24ToBgra32);
                var bgra2 = Ssse3.Shuffle(bgr2, shuffleBgr24ToBgra32);
                var bgra3 = Ssse3.Shuffle(bgr3, shuffleBgr24ToBgra32);

                var prod0 = Ssse3.MultiplyAddAdjacent(bgra0, coeffs128);
                var prod1 = Ssse3.MultiplyAddAdjacent(bgra1, coeffs128);
                var prod2 = Ssse3.MultiplyAddAdjacent(bgra2, coeffs128);
                var prod3 = Ssse3.MultiplyAddAdjacent(bgra3, coeffs128);

                var y0 = Sse2.MultiplyAddAdjacent(prod0, ones16_128);
                var y1 = Sse2.MultiplyAddAdjacent(prod1, ones16_128);
                var y2 = Sse2.MultiplyAddAdjacent(prod2, ones16_128);
                var y3 = Sse2.MultiplyAddAdjacent(prod3, ones16_128);

                y0 = Sse2.Add(y0, roundingBias32_128);
                y1 = Sse2.Add(y1, roundingBias32_128);
                y2 = Sse2.Add(y2, roundingBias32_128);
                y3 = Sse2.Add(y3, roundingBias32_128);

                y0 = Sse2.ShiftRightArithmetic(y0, 7);
                y1 = Sse2.ShiftRightArithmetic(y1, 7);
                y2 = Sse2.ShiftRightArithmetic(y2, 7);
                y3 = Sse2.ShiftRightArithmetic(y3, 7);

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
                var bgr = Sse2.LoadVector128(src);
                var bgra = Ssse3.Shuffle(bgr, shuffleBgr24ToBgra32);
                var prod = Ssse3.MultiplyAddAdjacent(bgra, coeffs128);
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
                var b = src[0];
                var g = src[1];
                var r = src[2];
                *dst++ = (byte)(((15 * b) + (75 * g) + (38 * r) + 64) >> 7);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Bgr24)

    /// <summary>
    /// AVX2: Gray8 → Bgr24.
    /// 32 пикселя за итерацию с настоящим AVX2 + SSE для записи 96 байт.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Avx2(ReadOnlySpan<Gray8> source, Span<Bgr24> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
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
                Sse2.StoreLow((double*)(dst + 16), rgb1.AsDouble());

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

    #region Conversion Operators

    /// <summary>Явное преобразование Bgr24 → Gray8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray8(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явное преобразование Gray8 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Gray8 gray) => gray.ToBgr24();

    #endregion
}
