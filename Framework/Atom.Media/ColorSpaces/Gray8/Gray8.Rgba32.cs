#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Rgba32.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Rgba32 в Gray8 (ITU-R BT.601, альфа игнорируется).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromRgba32(Rgba32 rgba)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y = ((19595 * rgba.R) + (38470 * rgba.G) + (7471 * rgba.B) + 32768) >> 16;
        return new((byte)y);
    }

    /// <summary>Конвертирует Gray8 в Rgba32 (R = G = B = Value, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32() => new(Value, Value, Value, 255);

    #endregion

    #region Batch Conversion (Gray8 → Rgba32)

    /// <summary>Пакетная конвертация Gray8 → Rgba32.</summary>
    public static void ToRgba32(ReadOnlySpan<Gray8> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Rgba32 с явным указанием ускорителя.</summary>
    public static unsafe void ToRgba32(ReadOnlySpan<Gray8> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
                ToRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToRgba32Core(ReadOnlySpan<Gray8> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            // AVX2 для Gray8→Rgba32 не даёт выигрыша (простая операция расширения 1→4 байт)
            // Используем SSE41 для всех SIMD путей
            case HardwareAcceleration.Avx2 when source.Length >= 16:
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToRgba32Sse41(source, destination);
                break;
            default:
                ToRgba32Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToRgba32Parallel(Gray8* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgba32Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Rgba32>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Rgba32 → Gray8)

    /// <summary>Пакетная конвертация Rgba32 → Gray8.</summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Gray8> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromRgba32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromRgba32Sse41(source, destination);
                break;
            default:
                FromRgba32Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromRgba32Parallel(Rgba32* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgba32Core(new ReadOnlySpan<Rgba32>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Scalar(ReadOnlySpan<Gray8> source, Span<Rgba32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (8 байт Gray8 → 32 байт Rgba32)
            while (count >= 8)
            {
                // Пиксель 0: Gray → R, G, B, A=255
                var g0 = src[0];
                dst[0] = g0; dst[1] = g0; dst[2] = g0; dst[3] = 255;

                // Пиксель 1
                var g1 = src[1];
                dst[4] = g1; dst[5] = g1; dst[6] = g1; dst[7] = 255;

                // Пиксель 2
                var g2 = src[2];
                dst[8] = g2; dst[9] = g2; dst[10] = g2; dst[11] = 255;

                // Пиксель 3
                var g3 = src[3];
                dst[12] = g3; dst[13] = g3; dst[14] = g3; dst[15] = 255;

                // Пиксель 4
                var g4 = src[4];
                dst[16] = g4; dst[17] = g4; dst[18] = g4; dst[19] = 255;

                // Пиксель 5
                var g5 = src[5];
                dst[20] = g5; dst[21] = g5; dst[22] = g5; dst[23] = 255;

                // Пиксель 6
                var g6 = src[6];
                dst[24] = g6; dst[25] = g6; dst[26] = g6; dst[27] = 255;

                // Пиксель 7
                var g7 = src[7];
                dst[28] = g7; dst[29] = g7; dst[30] = g7; dst[31] = 255;

                src += 8;
                dst += 32;
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                var g = src[0];
                dst[0] = g; dst[1] = g; dst[2] = g; dst[3] = 255;
                src++;
                dst += 4;
                count--;
            }
        }
    }

    // BT.601 коэффициенты для преобразования RGB → Gray (Q16 fixed-point)
    private const int GrayCoeffR = 19595;  // 0.299 × 65536
    private const int GrayCoeffG = 38470;  // 0.587 × 65536
    private const int GrayCoeffB = 7471;   // 0.114 × 65536
    private const int GrayHalf = 32768;    // 0.5 × 65536 для округления

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Scalar(ReadOnlySpan<Rgba32> source, Span<Gray8> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // 8 пикселей за итерацию (32 байт Rgba32 → 8 байт Gray8)
            while (count >= 8)
            {
                // Пиксель 0: Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
                dst[0] = (byte)(((GrayCoeffR * src[0]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[2]) + GrayHalf) >> 16);

                // Пиксель 1
                dst[1] = (byte)(((GrayCoeffR * src[4]) + (GrayCoeffG * src[5]) + (GrayCoeffB * src[6]) + GrayHalf) >> 16);

                // Пиксель 2
                dst[2] = (byte)(((GrayCoeffR * src[8]) + (GrayCoeffG * src[9]) + (GrayCoeffB * src[10]) + GrayHalf) >> 16);

                // Пиксель 3
                dst[3] = (byte)(((GrayCoeffR * src[12]) + (GrayCoeffG * src[13]) + (GrayCoeffB * src[14]) + GrayHalf) >> 16);

                // Пиксель 4
                dst[4] = (byte)(((GrayCoeffR * src[16]) + (GrayCoeffG * src[17]) + (GrayCoeffB * src[18]) + GrayHalf) >> 16);

                // Пиксель 5
                dst[5] = (byte)(((GrayCoeffR * src[20]) + (GrayCoeffG * src[21]) + (GrayCoeffB * src[22]) + GrayHalf) >> 16);

                // Пиксель 6
                dst[6] = (byte)(((GrayCoeffR * src[24]) + (GrayCoeffG * src[25]) + (GrayCoeffB * src[26]) + GrayHalf) >> 16);

                // Пиксель 7
                dst[7] = (byte)(((GrayCoeffR * src[28]) + (GrayCoeffG * src[29]) + (GrayCoeffB * src[30]) + GrayHalf) >> 16);

                src += 32;
                dst += 8;
                count -= 8;
            }

            // Остаток по 1 пикселю
            while (count > 0)
            {
                dst[0] = (byte)(((GrayCoeffR * src[0]) + (GrayCoeffG * src[1]) + (GrayCoeffB * src[2]) + GrayHalf) >> 16);
                src += 4;
                dst++;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Rgba32)

    /// <summary>
    /// SSE41: Gray8 → Rgba32.
    /// Дублирует Y в R, G, B; A = 255.
    /// 16 пикселей за итерацию с PUNPCK (без shift).
    /// Алгоритм: Y → [Y,Y] → [Y,Y,Y,0xFF]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Sse41(ReadOnlySpan<Gray8> source, Span<Rgba32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Константа 0xFF для альфа-канала
            var allFF = Gray8Sse41Vectors.AllFF;

            // 16 пикселей за итерацию с PUNPCK
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Шаг 1: Y → [Y,Y] self-interleave
                var yy_lo = Sse2.UnpackLow(gray, gray);   // [Y0,Y0,Y1,Y1,Y2,Y2,Y3,Y3]
                var yy_hi = Sse2.UnpackHigh(gray, gray);  // [Y8,Y8,Y9,Y9,Y10,Y10,Y11,Y11]

                // Шаг 2: Y + 0xFF → [Y,0xFF]
                var yFF_lo = Sse2.UnpackLow(gray, allFF); // [Y0,FF,Y1,FF,Y2,FF,Y3,FF]
                var yFF_hi = Sse2.UnpackHigh(gray, allFF);

                // Шаг 3: [Y,Y] + [Y,0xFF] → [Y,Y,Y,0xFF] as int16 pairs
                var rgba0 = Sse2.UnpackLow(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();   // пиксели 0-3
                var rgba1 = Sse2.UnpackHigh(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();  // пиксели 4-7
                var rgba2 = Sse2.UnpackLow(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();   // пиксели 8-11
                var rgba3 = Sse2.UnpackHigh(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();  // пиксели 12-15

                // Store 64 байт (16 RGBA пикселей)
                Sse2.Store(dst, rgba0);
                Sse2.Store(dst + 16, rgba1);
                Sse2.Store(dst + 32, rgba2);
                Sse2.Store(dst + 48, rgba3);

                src += 16;
                dst += 64;
                count -= 16;
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

    #region AVX2 Implementation (Gray8 → Rgba32)

    /// <summary>
    /// AVX2: Gray8 → Rgba32.
    /// 32 пикселя за итерацию с VPUNPCK.
    /// Алгоритм: Y → [Y,Y] → [Y,Y,Y,α] через 3-уровневый unpack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Avx2(ReadOnlySpan<Gray8> source, Span<Rgba32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Константы (кешированные)
            var allFF128 = Gray8Sse41Vectors.AllFF;

            // === 32 пикселя за итерацию (2× SSE обработка, AVX2 stores) ===
            // Без Permute2x128 — просто делаем SSE алгоритм дважды и используем AVX stores
            while (count >= 32)
            {
                // === Первые 16 пикселей (SSE алгоритм) ===
                var gray0 = Sse2.LoadVector128(src);

                var yy_lo0 = Sse2.UnpackLow(gray0, gray0);
                var yy_hi0 = Sse2.UnpackHigh(gray0, gray0);
                var yFF_lo0 = Sse2.UnpackLow(gray0, allFF128);
                var yFF_hi0 = Sse2.UnpackHigh(gray0, allFF128);

                var rgba0 = Sse2.UnpackLow(yy_lo0.AsInt16(), yFF_lo0.AsInt16()).AsByte();
                var rgba1 = Sse2.UnpackHigh(yy_lo0.AsInt16(), yFF_lo0.AsInt16()).AsByte();
                var rgba2 = Sse2.UnpackLow(yy_hi0.AsInt16(), yFF_hi0.AsInt16()).AsByte();
                var rgba3 = Sse2.UnpackHigh(yy_hi0.AsInt16(), yFF_hi0.AsInt16()).AsByte();

                // === Вторые 16 пикселей (SSE алгоритм) ===
                var gray1 = Sse2.LoadVector128(src + 16);

                var yy_lo1 = Sse2.UnpackLow(gray1, gray1);
                var yy_hi1 = Sse2.UnpackHigh(gray1, gray1);
                var yFF_lo1 = Sse2.UnpackLow(gray1, allFF128);
                var yFF_hi1 = Sse2.UnpackHigh(gray1, allFF128);

                var rgba4 = Sse2.UnpackLow(yy_lo1.AsInt16(), yFF_lo1.AsInt16()).AsByte();
                var rgba5 = Sse2.UnpackHigh(yy_lo1.AsInt16(), yFF_lo1.AsInt16()).AsByte();
                var rgba6 = Sse2.UnpackLow(yy_hi1.AsInt16(), yFF_hi1.AsInt16()).AsByte();
                var rgba7 = Sse2.UnpackHigh(yy_hi1.AsInt16(), yFF_hi1.AsInt16()).AsByte();

                // AVX2 stores: объединяем SSE пары в 256-bit и записываем
                var out0 = Avx.InsertVector128(rgba0.ToVector256Unsafe(), rgba1, 1);
                var out1 = Avx.InsertVector128(rgba2.ToVector256Unsafe(), rgba3, 1);
                var out2 = Avx.InsertVector128(rgba4.ToVector256Unsafe(), rgba5, 1);
                var out3 = Avx.InsertVector128(rgba6.ToVector256Unsafe(), rgba7, 1);

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

                var rgba0 = Sse2.UnpackLow(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var rgba1 = Sse2.UnpackHigh(yy_lo.AsInt16(), yFF_lo.AsInt16()).AsByte();
                var rgba2 = Sse2.UnpackLow(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();
                var rgba3 = Sse2.UnpackHigh(yy_hi.AsInt16(), yFF_hi.AsInt16()).AsByte();

                Sse2.Store(dst, rgba0);
                Sse2.Store(dst + 16, rgba1);
                Sse2.Store(dst + 32, rgba2);
                Sse2.Store(dst + 48, rgba3);

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

    #region SSE4.1 Implementation (Rgba32 → Gray8) — PMADDUBSW Pipeline

    /// <summary>
    /// SSE4.1: Rgba32 → Gray8 с использованием PMADDUBSW + PHADDW.
    /// Y = (38×R + 75×G + 15×B) >> 7 ≈ 0.297R + 0.586G + 0.117B
    ///
    /// Алгоритм (16 пикселей за итерацию):
    /// 1. PMADDUBSW: rgba[R,G,B,A] × coeffs[38,75,15,0] → [R×38+G×75, B×15+A×0] × 4 = 8 int16
    /// 2. PHADDW: горизонтальное сложение пар → Y × 4 (или 8) int16
    /// 3. Shift >>7 + PackUS → byte
    ///
    /// Коэффициенты Q7 (×128): 38 + 75 + 15 = 128 (все &lt;128, помещаются в sbyte!)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Sse41(ReadOnlySpan<Rgba32> source, Span<Gray8> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q7 коэффициенты для PMADDUBSW: [cR, cG, cB, 0] × 4 пикселя
            // 0.299×128=38.3≈38, 0.587×128=75.1≈75, 0.114×128=14.6≈15
            // Сумма: 38+75+15 = 128 → результат >>7 даёт точный Y
            var coeffs = Gray8Sse41Vectors.PmaddubswCoeffsRgba;
            // PMADDWD коэффициенты [1,1,1,1,1,1,1,1] для суммирования пар int16→int32
            var ones16 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32 для Q7
            var roundingBias32 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 16 пикселей за итерацию (PMADDUBSW + PMADDWD pipeline) ===
            while (count >= 16)
            {
                // Загружаем 64 байта (16 RGBA пикселей)
                var rgba0 = Sse2.LoadVector128(src);       // пиксели 0-3
                var rgba1 = Sse2.LoadVector128(src + 16);  // пиксели 4-7
                var rgba2 = Sse2.LoadVector128(src + 32);  // пиксели 8-11
                var rgba3 = Sse2.LoadVector128(src + 48);  // пиксели 12-15

                // PMADDUBSW: [R,G,B,A] × [38,75,15,0] → [R×38+G×75, B×15+0] × 4 = 8 int16
                var prod0 = Ssse3.MultiplyAddAdjacent(rgba0, coeffs);  // [rg0,ba0,rg1,ba1,rg2,ba2,rg3,ba3]
                var prod1 = Ssse3.MultiplyAddAdjacent(rgba1, coeffs);
                var prod2 = Ssse3.MultiplyAddAdjacent(rgba2, coeffs);
                var prod3 = Ssse3.MultiplyAddAdjacent(rgba3, coeffs);

                // PMADDWD: [rg,ba] × [1,1] → rg+ba в int32 (4 пикселя на вектор)
                // Это НАМНОГО быстрее чем PHADDW (1 uop vs 3 uops)
                var y0 = Sse2.MultiplyAddAdjacent(prod0, ones16);  // [Y0,Y1,Y2,Y3] int32
                var y1 = Sse2.MultiplyAddAdjacent(prod1, ones16);  // [Y4,Y5,Y6,Y7] int32
                var y2 = Sse2.MultiplyAddAdjacent(prod2, ones16);  // [Y8,Y9,Y10,Y11] int32
                var y3 = Sse2.MultiplyAddAdjacent(prod3, ones16);  // [Y12,Y13,Y14,Y15] int32

                // Добавляем rounding bias +64 (в int32)
                y0 = Sse2.Add(y0, roundingBias32);
                y1 = Sse2.Add(y1, roundingBias32);
                y2 = Sse2.Add(y2, roundingBias32);
                y3 = Sse2.Add(y3, roundingBias32);

                // Shift >>7 для деления на 128 (в int32)
                y0 = Sse2.ShiftRightArithmetic(y0, 7);
                y1 = Sse2.ShiftRightArithmetic(y1, 7);
                y2 = Sse2.ShiftRightArithmetic(y2, 7);
                y3 = Sse2.ShiftRightArithmetic(y3, 7);

                // Pack int32 → int16 (8 значений на вектор)
                var y01 = Sse2.PackSignedSaturate(y0, y1);  // [Y0..Y7] int16
                var y23 = Sse2.PackSignedSaturate(y2, y3);  // [Y8..Y15] int16

                // Pack int16 → uint8 (16 пикселей)
                var yBytes = Sse2.PackUnsignedSaturate(y01, y23);

                // Store 16 байт
                Sse2.Store(dst, yBytes);

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback (PMADDUBSW + PMADDWD)
            while (count >= 4)
            {
                var rgba = Sse2.LoadVector128(src);
                var prod = Ssse3.MultiplyAddAdjacent(rgba, coeffs);  // 8 int16
                var y = Sse2.MultiplyAddAdjacent(prod, ones16);      // 4 int32
                y = Sse2.Add(y, roundingBias32);
                y = Sse2.ShiftRightArithmetic(y, 7);
                var yPacked = Sse2.PackSignedSaturate(y, y);         // 8 int16
                var yBytes = Sse2.PackUnsignedSaturate(yPacked, yPacked);  // 16 uint8

                *(uint*)dst = yBytes.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Остаток (scalar)
            while (count > 0)
            {
                var r = src[0];
                var g = src[1];
                var b = src[2];
                // Q7: (38×R + 75×G + 15×B + 64) >> 7
                *dst++ = (byte)(((38 * r) + (75 * g) + (15 * b) + 64) >> 7);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Rgba32 → Gray8) — VPMADDUBSW Pipeline

    /// <summary>
    /// AVX2: Rgba32 → Gray8 с использованием VPMADDUBSW + VPHADDW.
    /// Y = (38×R + 75×G + 15×B) >> 7 ≈ 0.297R + 0.586G + 0.117B
    ///
    /// 32 пикселя за итерацию с полноценным AVX2 pipeline.
    /// Использует PMADDWD вместо PHADDW для максимальной производительности.
    /// Коэффициенты Q7 (все &lt;128) помещаются в sbyte.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Avx2(ReadOnlySpan<Rgba32> source, Span<Gray8> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // AVX2 коэффициенты Q7
            var coeffs256 = Gray8Avx2Vectors.PmaddubswCoeffsRgba;
            var coeffs128 = Gray8Sse41Vectors.PmaddubswCoeffsRgba;
            // PMADDWD коэффициенты [1,1,...] для суммирования пар int16→int32
            var ones16_256 = Gray8Avx2Vectors.Ones16;
            var ones16_128 = Gray8Sse41Vectors.Ones16;
            // Rounding bias 64 в int32
            var roundingBias32_256 = Gray8Avx2Vectors.RoundingBias64_Int32;
            var roundingBias32_128 = Gray8Sse41Vectors.RoundingBias64_Int32;

            // === 32 пикселя за итерацию (PMADDUBSW + PMADDWD pipeline) ===
            while (count >= 32)
            {
                // Загружаем 128 байт (32 RGBA пикселя) с AVX2
                var rgba0 = Avx.LoadVector256(src);        // пиксели 0-7
                var rgba1 = Avx.LoadVector256(src + 32);   // пиксели 8-15
                var rgba2 = Avx.LoadVector256(src + 64);   // пиксели 16-23
                var rgba3 = Avx.LoadVector256(src + 96);   // пиксели 24-31

                // VPMADDUBSW: [R,G,B,A] × [38,75,15,0] → [R×38+G×75, B×15+0] × 8 = 16 int16
                var prod0 = Avx2.MultiplyAddAdjacent(rgba0, coeffs256);
                var prod1 = Avx2.MultiplyAddAdjacent(rgba1, coeffs256);
                var prod2 = Avx2.MultiplyAddAdjacent(rgba2, coeffs256);
                var prod3 = Avx2.MultiplyAddAdjacent(rgba3, coeffs256);

                // VPMADDWD: [rg,ba] × [1,1] → rg+ba в int32 (8 пикселей на вектор)
                // Это НАМНОГО быстрее чем VPHADDW!
                var y0 = Avx2.MultiplyAddAdjacent(prod0, ones16_256);  // [Y0..Y7] int32
                var y1 = Avx2.MultiplyAddAdjacent(prod1, ones16_256);  // [Y8..Y15] int32
                var y2 = Avx2.MultiplyAddAdjacent(prod2, ones16_256);  // [Y16..Y23] int32
                var y3 = Avx2.MultiplyAddAdjacent(prod3, ones16_256);  // [Y24..Y31] int32

                // Добавляем rounding bias +64 (в int32)
                y0 = Avx2.Add(y0, roundingBias32_256);
                y1 = Avx2.Add(y1, roundingBias32_256);
                y2 = Avx2.Add(y2, roundingBias32_256);
                y3 = Avx2.Add(y3, roundingBias32_256);

                // Shift >>7 для деления на 128 (в int32)
                y0 = Avx2.ShiftRightArithmetic(y0, 7);
                y1 = Avx2.ShiftRightArithmetic(y1, 7);
                y2 = Avx2.ShiftRightArithmetic(y2, 7);
                y3 = Avx2.ShiftRightArithmetic(y3, 7);

                // Pack int32 → int16 (16 значений на вектор, но in-lane!)
                var y01 = Avx2.PackSignedSaturate(y0, y1);  // [Y0..Y7 | Y0..Y7] lanes
                var y23 = Avx2.PackSignedSaturate(y2, y3);  // [Y16..Y23 | Y16..Y23] lanes

                // Исправляем порядок после in-lane pack
                y01 = Avx2.Permute4x64(y01.AsInt64(), 0b11_01_10_00).AsInt16();
                y23 = Avx2.Permute4x64(y23.AsInt64(), 0b11_01_10_00).AsInt16();

                // Pack int16 → uint8 (32 пикселя)
                var yBytesPacked = Avx2.PackUnsignedSaturate(y01, y23);

                // Исправляем порядок после второго pack
                var yBytes = Avx2.Permute4x64(yBytesPacked.AsInt64(), 0b11_01_10_00).AsByte();

                // Store 32 байта
                Avx.Store(dst, yBytes);

                src += 128;
                dst += 32;
                count -= 32;
            }

            // 16 пикселей fallback (SSE с PMADDWD)
            while (count >= 16)
            {
                var rgba0 = Sse2.LoadVector128(src);
                var rgba1 = Sse2.LoadVector128(src + 16);
                var rgba2 = Sse2.LoadVector128(src + 32);
                var rgba3 = Sse2.LoadVector128(src + 48);

                var prod0 = Ssse3.MultiplyAddAdjacent(rgba0, coeffs128);
                var prod1 = Ssse3.MultiplyAddAdjacent(rgba1, coeffs128);
                var prod2 = Ssse3.MultiplyAddAdjacent(rgba2, coeffs128);
                var prod3 = Ssse3.MultiplyAddAdjacent(rgba3, coeffs128);

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

            // 4 пикселя fallback (PMADDWD)
            while (count >= 4)
            {
                var rgba = Sse2.LoadVector128(src);
                var prod = Ssse3.MultiplyAddAdjacent(rgba, coeffs128);
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

            // Остаток (scalar)
            while (count > 0)
            {
                var r = src[0];
                var g = src[1];
                var b = src[2];
                *dst++ = (byte)(((38 * r) + (75 * g) + (15 * b) + 64) >> 7);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Rgba32 → Gray8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray8(Rgba32 rgba) => FromRgba32(rgba);

    /// <summary>Явное преобразование Gray8 → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Gray8 gray) => gray.ToRgba32();

    #endregion
}
