#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Gray16.
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants

    private const HardwareAcceleration Gray16Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Cmyk.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromGray16(Gray16 gray)
    {
        // K = 255 - (Value >> 8)
        var k = (byte)(255 - (gray.Value >> 8));
        return new(0, 0, 0, k);
    }

    /// <summary>Конвертирует Cmyk в Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16()
    {
        // Через Gray8, затем расширяем до 16 бит
        var gray8 = ToGray8();
        return new((ushort)(gray8.Value * 257));
    }

    #endregion

    #region Batch Conversion (Cmyk → Gray16)

    /// <summary>Пакетная конвертация Cmyk → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Cmyk> source, Span<Gray16> destination) =>
        ToGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray16(ReadOnlySpan<Cmyk> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                ToGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray16Core(ReadOnlySpan<Cmyk> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToGray16Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToGray16Sse41(source, destination);
                break;
            default:
                ToGray16Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToGray16Parallel(Cmyk* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Cmyk>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray16Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray16 → Cmyk)

    /// <summary>Пакетная конвертация Gray16 → Cmyk.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Cmyk> destination) =>
        FromGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Cmyk с явным указанием ускорителя.</summary>
    public static unsafe void FromGray16(ReadOnlySpan<Gray16> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
                FromGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray16Core(ReadOnlySpan<Gray16> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromGray16Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromGray16Sse41(source, destination);
                break;
            default:
                FromGray16Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromGray16Parallel(Gray16* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Cmyk>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray16Core(new ReadOnlySpan<Gray16>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray16Scalar(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToGray16();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromGray16Scalar(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray16(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Cmyk → Gray16)

    /// <summary>
    /// SSE41: Cmyk → Gray16.
    /// CMYK → RGB → Gray16.
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Sse41(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            var allFF = CmykSse41Vectors.AllFF;
            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;

            var coefR = Gray8Sse41Vectors.CoefficientR_Q16;
            var coefG = Gray8Sse41Vectors.CoefficientG_Q16;
            var coefB = Gray8Sse41Vectors.CoefficientB_Q16;
            var half = Gray8Sse41Vectors.Half;

            var mult257 = CmykSse41Vectors.Mult257I;
            var one = CmykSse41Vectors.C1I;

            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);

                var c = Ssse3.Shuffle(cmyk, shuffleC);
                var m = Ssse3.Shuffle(cmyk, shuffleM);
                var y = Ssse3.Shuffle(cmyk, shuffleY);
                var k = Ssse3.Shuffle(cmyk, shuffleK);

                var invC = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, c));
                var invM = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, m));
                var invY = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, y));
                var invK = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, k));

                // Деление на 255: (x + 1 + ((x + 1) >> 8)) >> 8
                var rProd = Sse41.MultiplyLow(invC, invK);
                var gProd = Sse41.MultiplyLow(invM, invK);
                var bProd = Sse41.MultiplyLow(invY, invK);

                var rProd1 = Sse2.Add(rProd, one);
                var gProd1 = Sse2.Add(gProd, one);
                var bProd1 = Sse2.Add(bProd, one);

                var r = Sse2.ShiftRightLogical(Sse2.Add(rProd1, Sse2.ShiftRightLogical(rProd1, 8)), 8);
                var g = Sse2.ShiftRightLogical(Sse2.Add(gProd1, Sse2.ShiftRightLogical(gProd1, 8)), 8);
                var b = Sse2.ShiftRightLogical(Sse2.Add(bProd1, Sse2.ShiftRightLogical(bProd1, 8)), 8);

                // Gray = BT.601
                var gray = Sse41.MultiplyLow(r, coefR);
                gray = Sse2.Add(gray, Sse41.MultiplyLow(g, coefG));
                gray = Sse2.Add(gray, Sse41.MultiplyLow(b, coefB));
                gray = Sse2.Add(gray, half);
                gray = Sse2.ShiftRightLogical(gray, 16);

                // Gray8 → Gray16: × 257
                var gray16 = Sse41.MultiplyLow(gray, mult257);

                var packed = Sse41.PackUnsignedSaturate(gray16, gray16);

                *(ulong*)dst = packed.AsUInt64().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = cmyk.ToGray16().Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Cmyk → Gray16)

    /// <summary>
    /// AVX2: Cmyk → Gray16.
    /// 8 пикселей за итерацию с настоящими 256-bit операциями.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Avx2(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // SSE константы
            var allFF = CmykSse41Vectors.AllFF;
            var shuffleC128 = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM128 = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY128 = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK128 = CmykSse41Vectors.ShuffleCmykK;

            var coefR = Gray8Sse41Vectors.CoefficientR_Q16;
            var coefG = Gray8Sse41Vectors.CoefficientG_Q16;
            var coefB = Gray8Sse41Vectors.CoefficientB_Q16;
            var half = Gray8Sse41Vectors.Half;

            var mult257_128 = CmykSse41Vectors.Mult257I;

            // AVX2 256-bit Q16 коэффициенты
            var coefR256 = Gray8Avx2Vectors.CoefficientR_Q16;
            var coefG256 = Gray8Avx2Vectors.CoefficientG_Q16;
            var coefB256 = Gray8Avx2Vectors.CoefficientB_Q16;
            var half256 = Gray8Avx2Vectors.Half_Q16;
            var mult257_256 = CmykAvx2Vectors.Mult257I;
            var one256 = CmykAvx2Vectors.C1I;
            var one128 = CmykSse41Vectors.C1I;

            // === 8 пикселей за итерацию (32 байта CMYK → 16 байт Gray16) ===
            while (count >= 8)
            {
                // Загружаем 2 блока по 4 пикселя (32 байт = 8 CMYK)
                var cmyk0 = Sse2.LoadVector128(src);
                var cmyk1 = Sse2.LoadVector128(src + 16);

                // Деинтерливинг CMYK (SSE)
                var c0 = Ssse3.Shuffle(cmyk0, shuffleC128);
                var m0 = Ssse3.Shuffle(cmyk0, shuffleM128);
                var y0 = Ssse3.Shuffle(cmyk0, shuffleY128);
                var k0 = Ssse3.Shuffle(cmyk0, shuffleK128);

                var c1 = Ssse3.Shuffle(cmyk1, shuffleC128);
                var m1 = Ssse3.Shuffle(cmyk1, shuffleM128);
                var y1 = Ssse3.Shuffle(cmyk1, shuffleY128);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK128);

                // Инвертируем: 255 - x
                var invC0 = Sse2.Subtract(allFF, c0);
                var invM0 = Sse2.Subtract(allFF, m0);
                var invY0 = Sse2.Subtract(allFF, y0);
                var invK0 = Sse2.Subtract(allFF, k0);

                var invC1 = Sse2.Subtract(allFF, c1);
                var invM1 = Sse2.Subtract(allFF, m1);
                var invY1 = Sse2.Subtract(allFF, y1);
                var invK1 = Sse2.Subtract(allFF, k1);

                // Расширяем до int32 и объединяем в AVX2 256-bit
                var invC_256 = Vector256.Create(Sse41.ConvertToVector128Int32(invC0), Sse41.ConvertToVector128Int32(invC1));
                var invM_256 = Vector256.Create(Sse41.ConvertToVector128Int32(invM0), Sse41.ConvertToVector128Int32(invM1));
                var invY_256 = Vector256.Create(Sse41.ConvertToVector128Int32(invY0), Sse41.ConvertToVector128Int32(invY1));
                var invK_256 = Vector256.Create(Sse41.ConvertToVector128Int32(invK0), Sse41.ConvertToVector128Int32(invK1));

                // RGB = (255-C)*(255-K)/255, деление на 255: (x + 1 + ((x + 1) >> 8)) >> 8
                var rProd256 = Avx2.MultiplyLow(invC_256, invK_256);
                var gProd256 = Avx2.MultiplyLow(invM_256, invK_256);
                var bProd256 = Avx2.MultiplyLow(invY_256, invK_256);

                var rProd1_256 = Avx2.Add(rProd256, one256);
                var gProd1_256 = Avx2.Add(gProd256, one256);
                var bProd1_256 = Avx2.Add(bProd256, one256);

                var r256 = Avx2.ShiftRightLogical(Avx2.Add(rProd1_256, Avx2.ShiftRightLogical(rProd1_256, 8)), 8);
                var g256 = Avx2.ShiftRightLogical(Avx2.Add(gProd1_256, Avx2.ShiftRightLogical(gProd1_256, 8)), 8);
                var b256 = Avx2.ShiftRightLogical(Avx2.Add(bProd1_256, Avx2.ShiftRightLogical(bProd1_256, 8)), 8);

                // Gray = BT.601: 0.299*R + 0.587*G + 0.114*B (Q16)
                var gray256 = Avx2.MultiplyLow(r256, coefR256);
                gray256 = Avx2.Add(gray256, Avx2.MultiplyLow(g256, coefG256));
                gray256 = Avx2.Add(gray256, Avx2.MultiplyLow(b256, coefB256));
                gray256 = Avx2.Add(gray256, half256);
                gray256 = Avx2.ShiftRightLogical(gray256, 16);

                // Gray8 → Gray16: × 257
                var gray16_256 = Avx2.MultiplyLow(gray256, mult257_256);

                // Упаковка int32 → uint16 (8 значений)
                var packed = Sse41.PackUnsignedSaturate(gray16_256.GetLower(), gray16_256.GetUpper());
                Sse2.Store(dst, packed);

                src += 32;
                dst += 8;
                count -= 8;
            }

            // 4 пикселя fallback (SSE)
            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);

                var c = Ssse3.Shuffle(cmyk, shuffleC128);
                var m = Ssse3.Shuffle(cmyk, shuffleM128);
                var y = Ssse3.Shuffle(cmyk, shuffleY128);
                var k = Ssse3.Shuffle(cmyk, shuffleK128);

                var invC = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, c));
                var invM = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, m));
                var invY = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, y));
                var invK = Sse41.ConvertToVector128Int32(Sse2.Subtract(allFF, k));

                // RGB = (255-C)*(255-K)/255, деление на 255: (x + 1 + ((x + 1) >> 8)) >> 8
                var rProd = Sse41.MultiplyLow(invC, invK);
                var gProd = Sse41.MultiplyLow(invM, invK);
                var bProd = Sse41.MultiplyLow(invY, invK);

                var rProd1 = Sse2.Add(rProd, one128);
                var gProd1 = Sse2.Add(gProd, one128);
                var bProd1 = Sse2.Add(bProd, one128);

                var r = Sse2.ShiftRightLogical(Sse2.Add(rProd1, Sse2.ShiftRightLogical(rProd1, 8)), 8);
                var g = Sse2.ShiftRightLogical(Sse2.Add(gProd1, Sse2.ShiftRightLogical(gProd1, 8)), 8);
                var b = Sse2.ShiftRightLogical(Sse2.Add(bProd1, Sse2.ShiftRightLogical(bProd1, 8)), 8);

                var gray = Sse41.MultiplyLow(r, coefR);
                gray = Sse2.Add(gray, Sse41.MultiplyLow(g, coefG));
                gray = Sse2.Add(gray, Sse41.MultiplyLow(b, coefB));
                gray = Sse2.Add(gray, half);
                gray = Sse2.ShiftRightLogical(gray, 16);

                var gray16 = Sse41.MultiplyLow(gray, mult257_128);
                var packed = Sse41.PackUnsignedSaturate(gray16, gray16);

                *(ulong*)dst = packed.AsUInt64().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = cmyk.ToGray16().Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray16 → Cmyk)

    /// <summary>
    /// SSE41: Gray16 → Cmyk.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Sse41(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем статические маски
            var shuffleHigh = CmykSse41Vectors.ShuffleGray16HighByte;
            var allFF = CmykSse41Vectors.AllFF;
            var shuffle = CmykSse41Vectors.ShuffleGray8KToCmyk;

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);

                // K = 255 - Gray
                var k = Sse2.Subtract(allFF, gray8);

                var k0 = k;
                var k1 = Sse2.ShiftRightLogical128BitLane(k, 4);

                Ssse3.Shuffle(k0, shuffle).Store(dst);
                Ssse3.Shuffle(k1, shuffle).Store(dst + 16);

                src += 8;
                dst += 32;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                src++;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray16 → Cmyk)

    /// <summary>
    /// AVX2: Gray16 → Cmyk.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Avx2(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем статические маски
            var shuffleHigh = CmykSse41Vectors.ShuffleGray16HighByte;
            var allFF = CmykSse41Vectors.AllFF;
            var shuffle = CmykSse41Vectors.ShuffleGray8KToCmyk;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var gray16_0 = Sse2.LoadVector128(src);
                var gray8_0 = Ssse3.Shuffle(gray16_0.AsByte(), shuffleHigh);
                var k0 = Sse2.Subtract(allFF, gray8_0);

                var k0_0 = k0;
                var k0_1 = Sse2.ShiftRightLogical128BitLane(k0, 4);

                Ssse3.Shuffle(k0_0, shuffle).Store(dst);
                Ssse3.Shuffle(k0_1, shuffle).Store(dst + 16);

                // Вторые 8 пикселей
                var gray16_1 = Sse2.LoadVector128(src + 8);
                var gray8_1 = Ssse3.Shuffle(gray16_1.AsByte(), shuffleHigh);
                var k1 = Sse2.Subtract(allFF, gray8_1);

                var k1_0 = k1;
                var k1_1 = Sse2.ShiftRightLogical128BitLane(k1, 4);

                Ssse3.Shuffle(k1_0, shuffle).Store(dst + 32);
                Ssse3.Shuffle(k1_1, shuffle).Store(dst + 48);

                src += 16;
                dst += 64;
                count -= 16;
            }

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);
                var k = Sse2.Subtract(allFF, gray8);

                var k_0 = k;
                var k_1 = Sse2.ShiftRightLogical128BitLane(k, 4);

                Ssse3.Shuffle(k_0, shuffle).Store(dst);
                Ssse3.Shuffle(k_1, shuffle).Store(dst + 16);

                src += 8;
                dst += 32;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                src++;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Cmyk(Gray16 gray) => FromGray16(gray);
    public static explicit operator Gray16(Cmyk cmyk) => cmyk.ToGray16();

    #endregion
}
