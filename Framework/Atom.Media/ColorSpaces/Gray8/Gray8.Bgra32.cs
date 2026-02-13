#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Bgra32.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration Bgra32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Bgra32 в Gray8 (ITU-R BT.601, альфа игнорируется).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromBgra32(Bgra32 bgra)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y = ((19595 * bgra.R) + (38470 * bgra.G) + (7471 * bgra.B) + 32768) >> 16;
        return new((byte)y);
    }

    /// <summary>Конвертирует Gray8 в Bgra32 (B = G = R = Value, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32() => new(Value, Value, Value, 255);

    #endregion

    #region Batch Conversion (Gray8 → Bgra32)

    /// <summary>Пакетная конвертация Gray8 → Bgra32.</summary>
    public static void ToBgra32(ReadOnlySpan<Gray8> source, Span<Bgra32> destination) =>
        ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Bgra32 с явным указанием ускорителя.</summary>
    public static unsafe void ToBgra32(ReadOnlySpan<Gray8> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
                ToBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToBgra32Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToBgra32Core(ReadOnlySpan<Gray8> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToBgra32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToBgra32Sse41(source, destination);
                break;
            default:
                ToBgra32Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToBgra32Parallel(Gray8* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgra32Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Bgra32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Bgra32 → Gray8)

    /// <summary>Пакетная конвертация Bgra32 → Gray8.</summary>
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Gray8> destination) =>
        FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromBgra32Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromBgra32Core(ReadOnlySpan<Bgra32> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromBgra32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromBgra32Sse41(source, destination);
                break;
            default:
                FromBgra32Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromBgra32Parallel(Bgra32* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgra32Core(new ReadOnlySpan<Bgra32>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Scalar(ReadOnlySpan<Gray8> source, Span<Bgra32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (8 байт Gray8 → 32 байта Bgra32)
            while (count >= 8)
            {
                var g0 = src[0]; var g1 = src[1]; var g2 = src[2]; var g3 = src[3];
                var g4 = src[4]; var g5 = src[5]; var g6 = src[6]; var g7 = src[7];

                dst[0] = g0; dst[1] = g0; dst[2] = g0; dst[3] = 255;
                dst[4] = g1; dst[5] = g1; dst[6] = g1; dst[7] = 255;
                dst[8] = g2; dst[9] = g2; dst[10] = g2; dst[11] = 255;
                dst[12] = g3; dst[13] = g3; dst[14] = g3; dst[15] = 255;
                dst[16] = g4; dst[17] = g4; dst[18] = g4; dst[19] = 255;
                dst[20] = g5; dst[21] = g5; dst[22] = g5; dst[23] = 255;
                dst[24] = g6; dst[25] = g6; dst[26] = g6; dst[27] = 255;
                dst[28] = g7; dst[29] = g7; dst[30] = g7; dst[31] = 255;

                src += 8;
                dst += 32;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                var g = *src++;
                *dst++ = g; *dst++ = g; *dst++ = g; *dst++ = 255;
                count--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Scalar(ReadOnlySpan<Bgra32> source, Span<Gray8> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (32 байта Bgra32 → 8 байт Gray8)
            // BGRA порядок: B=src[0], G=src[1], R=src[2], A=src[3]
            while (count >= 8)
            {
                dst[0] = (byte)(((GrayCoeffR * src[2]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[0]) + GrayHalf) >> 16);
                dst[1] = (byte)(((GrayCoeffR * src[6]) + (GrayCoeffG * src[5]) + (GrayCoeffB * src[4]) + GrayHalf) >> 16);
                dst[2] = (byte)(((GrayCoeffR * src[10]) + (GrayCoeffG * src[9]) + (GrayCoeffB * src[8]) + GrayHalf) >> 16);
                dst[3] = (byte)(((GrayCoeffR * src[14]) + (GrayCoeffG * src[13]) + (GrayCoeffB * src[12]) + GrayHalf) >> 16);
                dst[4] = (byte)(((GrayCoeffR * src[18]) + (GrayCoeffG * src[17]) + (GrayCoeffB * src[16]) + GrayHalf) >> 16);
                dst[5] = (byte)(((GrayCoeffR * src[22]) + (GrayCoeffG * src[21]) + (GrayCoeffB * src[20]) + GrayHalf) >> 16);
                dst[6] = (byte)(((GrayCoeffR * src[26]) + (GrayCoeffG * src[25]) + (GrayCoeffB * src[24]) + GrayHalf) >> 16);
                dst[7] = (byte)(((GrayCoeffR * src[30]) + (GrayCoeffG * src[29]) + (GrayCoeffB * src[28]) + GrayHalf) >> 16);

                src += 32;
                dst += 8;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                *dst++ = (byte)(((GrayCoeffR * src[2]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[0]) + GrayHalf) >> 16);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Bgra32)

    /// <summary>
    /// AVX2: Gray8 → Bgra32.
    /// 32 пикселя за итерацию с VPUNPCK.
    /// Алгоритм: Y → [Y,Y] → [Y,Y,Y,α] через 3-уровневый unpack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Avx2(ReadOnlySpan<Gray8> source, Span<Bgra32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Константы (кешированные)
            var allFF256 = Gray8Avx2Vectors.AllFF;
            var allFF128 = Gray8Sse41Vectors.AllFF;

            // === 32 пикселя за итерацию с AVX2 ===
            while (count >= 32)
            {
                // Загружаем 32 Gray8 пикселя
                var gray = Avx.LoadVector256(src);

                // Шаг 1: Y → [Y,Y] self-interleave (in-lane)
                var yy_lo = Avx2.UnpackLow(gray, gray);   // [Y0,Y0,Y1,Y1,...] в каждой lane
                var yy_hi = Avx2.UnpackHigh(gray, gray);

                // Шаг 2: Y + 0xFF → [Y,0xFF]
                var yFF_lo = Avx2.UnpackLow(gray, allFF256);
                var yFF_hi = Avx2.UnpackHigh(gray, allFF256);

                // Шаг 3: [Y,Y] + [Y,0xFF] → [Y,Y,Y,0xFF]
                var bgra0 = Avx2.UnpackLow(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();   // пиксели 0-3, 16-19
                var bgra1 = Avx2.UnpackHigh(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();  // пиксели 4-7, 20-23
                var bgra2 = Avx2.UnpackLow(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();   // пиксели 8-11, 24-27
                var bgra3 = Avx2.UnpackHigh(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();  // пиксели 12-15, 28-31

                // AVX2 unpack работает in-lane, нужно переставить
                // bgra0 = [px0-3 | px16-19], bgra1 = [px4-7 | px20-23], etc.
                // Нужно: [px0-3 | px4-7], [px8-11 | px12-15], [px16-19 | px20-23], [px24-27 | px28-31]
                var out0 = Avx2.Permute2x128(bgra0, bgra1, 0x20); // low lanes: px0-7
                var out1 = Avx2.Permute2x128(bgra2, bgra3, 0x20); // low lanes: px8-15
                var out2 = Avx2.Permute2x128(bgra0, bgra1, 0x31); // high lanes: px16-23
                var out3 = Avx2.Permute2x128(bgra2, bgra3, 0x31); // high lanes: px24-31

                // Store 128 байт (32 BGRA пикселя)
                Avx.Store(dst, out0);
                Avx.Store(dst + 32, out1);
                Avx.Store(dst + 64, out2);
                Avx.Store(dst + 96, out3);

                src += 32;
                dst += 128;
                count -= 32;
            }

            // 16 пикселей SSE fallback
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                var yy_lo = Sse2.UnpackLow(gray, gray);
                var yy_hi = Sse2.UnpackHigh(gray, gray);
                var yFF_lo = Sse2.UnpackLow(gray, allFF128);
                var yFF_hi = Sse2.UnpackHigh(gray, allFF128);

                var bgra0 = Sse2.UnpackLow(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var bgra1 = Sse2.UnpackHigh(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var bgra2 = Sse2.UnpackLow(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();
                var bgra3 = Sse2.UnpackHigh(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();

                Sse2.Store(dst, bgra0);
                Sse2.Store(dst + 16, bgra1);
                Sse2.Store(dst + 32, bgra2);
                Sse2.Store(dst + 48, bgra3);

                src += 16;
                dst += 64;
                count -= 16;
            }

            // 4 пикселя fallback
            while (count >= 4)
            {
                var y0 = src[0]; var y1 = src[1]; var y2 = src[2]; var y3 = src[3];
                dst[0] = y0; dst[1] = y0; dst[2] = y0; dst[3] = 255;
                dst[4] = y1; dst[5] = y1; dst[6] = y1; dst[7] = 255;
                dst[8] = y2; dst[9] = y2; dst[10] = y2; dst[11] = 255;
                dst[12] = y3; dst[13] = y3; dst[14] = y3; dst[15] = 255;
                src += 4;
                dst += 16;
                count -= 4;
            }

            // Остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = v;
                *dst++ = v;
                *dst++ = 255;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Bgra32)

    /// <summary>
    /// SSE41: Gray8 → Bgra32.
    /// Дублирует Y в B, G, R; A = 255.
    /// 16 пикселей за итерацию с PUNPCK (без shift).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Sse41(ReadOnlySpan<Gray8> source, Span<Bgra32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Константа 0xFF для альфа-канала (кешированная)
            var allFF = Gray8Sse41Vectors.AllFF;

            // 16 пикселей за итерацию с PUNPCK
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Шаг 1: Y → [Y,Y] self-interleave
                var yy_lo = Sse2.UnpackLow(gray, gray);
                var yy_hi = Sse2.UnpackHigh(gray, gray);

                // Шаг 2: Y + 0xFF → [Y,0xFF]
                var yFF_lo = Sse2.UnpackLow(gray, allFF);
                var yFF_hi = Sse2.UnpackHigh(gray, allFF);

                // Шаг 3: [Y,Y] + [Y,0xFF] → [Y,Y,Y,0xFF]
                var bgra0 = Sse2.UnpackLow(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var bgra1 = Sse2.UnpackHigh(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var bgra2 = Sse2.UnpackLow(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();
                var bgra3 = Sse2.UnpackHigh(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();

                Sse2.Store(dst, bgra0);
                Sse2.Store(dst + 16, bgra1);
                Sse2.Store(dst + 32, bgra2);
                Sse2.Store(dst + 48, bgra3);

                src += 16;
                dst += 64;
                count -= 16;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = v;
                *dst++ = v;
                *dst++ = 255;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Bgra32 → Gray8) — PMADDUBSW Pipeline

    /// <summary>
    /// SSE41: Bgra32 → Gray8 с использованием PMADDUBSW + PMADDWD.
    /// Y = (15×B + 75×G + 38×R) >> 7 ≈ 0.117B + 0.586G + 0.297R
    /// 16 пикселей за итерацию с минимальным количеством инструкций.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Sse41(ReadOnlySpan<Bgra32> source, Span<Gray8> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q7 коэффициенты для PMADDUBSW: [cB, cG, cR, 0] × 4 пикселя
            // BGRA порядок: B=15, G=75, R=38, A=0
            var coeffs = Gray8Sse41Vectors.PmaddubswCoeffsBgra;
            // PMADDWD коэффициенты [1,1,...] для суммирования пар int16→int32
            var ones16 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32 для Q7
            var roundingBias32 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 16 пикселей за итерацию (PMADDUBSW + PMADDWD pipeline) ===
            while (count >= 16)
            {
                // Загружаем 64 байта (16 BGRA пикселей)
                var bgra0 = Sse2.LoadVector128(src);       // пиксели 0-3
                var bgra1 = Sse2.LoadVector128(src + 16);  // пиксели 4-7
                var bgra2 = Sse2.LoadVector128(src + 32);  // пиксели 8-11
                var bgra3 = Sse2.LoadVector128(src + 48);  // пиксели 12-15

                // PMADDUBSW: [B,G,R,A] × [15,75,38,0] → [B×15+G×75, R×38+0] × 4 = 8 int16
                var prod0 = Ssse3.MultiplyAddAdjacent(bgra0, coeffs);
                var prod1 = Ssse3.MultiplyAddAdjacent(bgra1, coeffs);
                var prod2 = Ssse3.MultiplyAddAdjacent(bgra2, coeffs);
                var prod3 = Ssse3.MultiplyAddAdjacent(bgra3, coeffs);

                // PMADDWD: [bg,ra] × [1,1] → bg+ra в int32
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

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            while (count >= 4)
            {
                var bgra = Sse2.LoadVector128(src);
                var prod = Ssse3.MultiplyAddAdjacent(bgra, coeffs);
                var y = Sse2.MultiplyAddAdjacent(prod, ones16);
                y = Sse2.Add(y, roundingBias32);
                y = Sse2.ShiftRightArithmetic(y, 7);
                var yPacked = Sse2.PackSignedSaturate(y, y);
                var yBytes = Sse2.PackUnsignedSaturate(yPacked, yPacked);

                *(uint*)dst = yBytes.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var b = src[0];
                var g = src[1];
                var r = src[2];
                // Q7: (15×B + 75×G + 38×R + 64) >> 7
                *dst++ = (byte)(((15 * b) + (75 * g) + (38 * r) + 64) >> 7);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Bgra32 → Gray8) — VPMADDUBSW Pipeline

    /// <summary>
    /// AVX2: Bgra32 → Gray8 с использованием VPMADDUBSW + VPMADDWD.
    /// Y = (15×B + 75×G + 38×R) >> 7 ≈ 0.117B + 0.586G + 0.297R
    /// 32 пикселя за итерацию с полноценным AVX2 pipeline.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Avx2(ReadOnlySpan<Bgra32> source, Span<Gray8> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // AVX2 Q7 коэффициенты
            var coeffs256 = Gray8Avx2Vectors.PmaddubswCoeffsBgra;
            var coeffs128 = Gray8Sse41Vectors.PmaddubswCoeffsBgra;
            // PMADDWD коэффициенты [1,1,...] для суммирования пар
            var ones16_256 = Gray8Avx2Vectors.Ones16;
            var ones16_128 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32
            var roundingBias32_256 = Gray8Avx2Vectors.RoundingBias64_Int32;
            var roundingBias32_128 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 32 пикселя за итерацию (PMADDUBSW + PMADDWD pipeline) ===
            while (count >= 32)
            {
                // Загружаем 128 байт (32 BGRA пикселя)
                var bgra0 = Avx.LoadVector256(src);
                var bgra1 = Avx.LoadVector256(src + 32);
                var bgra2 = Avx.LoadVector256(src + 64);
                var bgra3 = Avx.LoadVector256(src + 96);

                // VPMADDUBSW
                var prod0 = Avx2.MultiplyAddAdjacent(bgra0, coeffs256);
                var prod1 = Avx2.MultiplyAddAdjacent(bgra1, coeffs256);
                var prod2 = Avx2.MultiplyAddAdjacent(bgra2, coeffs256);
                var prod3 = Avx2.MultiplyAddAdjacent(bgra3, coeffs256);

                // VPMADDWD
                var y0 = Avx2.MultiplyAddAdjacent(prod0, ones16_256);
                var y1 = Avx2.MultiplyAddAdjacent(prod1, ones16_256);
                var y2 = Avx2.MultiplyAddAdjacent(prod2, ones16_256);
                var y3 = Avx2.MultiplyAddAdjacent(prod3, ones16_256);

                // Rounding + shift
                y0 = Avx2.Add(y0, roundingBias32_256);
                y1 = Avx2.Add(y1, roundingBias32_256);
                y2 = Avx2.Add(y2, roundingBias32_256);
                y3 = Avx2.Add(y3, roundingBias32_256);

                y0 = Avx2.ShiftRightArithmetic(y0, 7);
                y1 = Avx2.ShiftRightArithmetic(y1, 7);
                y2 = Avx2.ShiftRightArithmetic(y2, 7);
                y3 = Avx2.ShiftRightArithmetic(y3, 7);

                // Pack int32 → int16 (in-lane)
                var y01 = Avx2.PackSignedSaturate(y0, y1);
                var y23 = Avx2.PackSignedSaturate(y2, y3);

                // Исправляем порядок после in-lane pack
                y01 = Avx2.Permute4x64(y01.AsInt64(), 0b11_01_10_00).AsInt16();
                y23 = Avx2.Permute4x64(y23.AsInt64(), 0b11_01_10_00).AsInt16();

                // Pack int16 → uint8
                var yBytesPacked = Avx2.PackUnsignedSaturate(y01, y23);
                var yBytes = Avx2.Permute4x64(yBytesPacked.AsInt64(), 0b11_01_10_00).AsByte();

                Avx.Store(dst, yBytes);

                src += 128;
                dst += 32;
                count -= 32;
            }

            // 16 пикселей fallback (SSE)
            while (count >= 16)
            {
                var bgra0 = Sse2.LoadVector128(src);
                var bgra1 = Sse2.LoadVector128(src + 16);
                var bgra2 = Sse2.LoadVector128(src + 32);
                var bgra3 = Sse2.LoadVector128(src + 48);

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

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            while (count >= 4)
            {
                var bgra = Sse2.LoadVector128(src);
                var prod = Ssse3.MultiplyAddAdjacent(bgra, coeffs128);
                var y = Sse2.MultiplyAddAdjacent(prod, ones16_128);
                y = Sse2.Add(y, roundingBias32_128);
                y = Sse2.ShiftRightArithmetic(y, 7);
                var yPacked = Sse2.PackSignedSaturate(y, y);
                var yBytes = Sse2.PackUnsignedSaturate(yPacked, yPacked);

                *(uint*)dst = yBytes.AsUInt32().GetElement(0);

                src += 16;
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
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgra32 → Gray8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray8(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явное преобразование Gray8 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(Gray8 gray) => gray.ToBgra32();

    #endregion
}
