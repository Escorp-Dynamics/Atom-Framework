#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Cmyk ↔ Gray8.
/// </summary>
public readonly partial struct Cmyk
{
    #region SIMD Constants

    private const HardwareAcceleration Gray8Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Cmyk (C = M = Y = 0, K = 255 - Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Cmyk FromGray8(Gray8 gray) => new(0, 0, 0, (byte)(255 - gray.Value));

    /// <summary>Конвертирует Cmyk в Gray8 (через формулу K).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8()
    {
        // Gray = 255 - K (упрощённо, игнорируем CMY для чистого grayscale)
        // Для более точного: через RGB
        var rgb = ToRgb24();
        return Gray8.FromRgb24(rgb);
    }

    #endregion

    #region Batch Conversion (Cmyk → Gray8)

    /// <summary>Пакетная конвертация Cmyk → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Cmyk> source, Span<Gray8> destination) =>
        ToGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray8(ReadOnlySpan<Cmyk> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                ToGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray8Core(ReadOnlySpan<Cmyk> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToGray8Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToGray8Sse41(source, destination);
                break;
            default:
                ToGray8Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToGray8Parallel(Cmyk* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Cmyk>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray8Core(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray8 → Cmyk)

    /// <summary>Пакетная конвертация Gray8 → Cmyk.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Cmyk> destination) =>
        FromGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Cmyk с явным указанием ускорителя.</summary>
    public static unsafe void FromGray8(ReadOnlySpan<Gray8> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
                FromGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray8Core(ReadOnlySpan<Gray8> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromGray8Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                FromGray8Sse41(source, destination);
                break;
            default:
                FromGray8Scalar(source, destination);
                break;
        }
    }

    private static unsafe void FromGray8Parallel(Gray8* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Cmyk>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray8Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray8Scalar(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToGray8();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromGray8Scalar(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray8(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Cmyk → Gray8)

    /// <summary>
    /// SSE41: Cmyk → Gray8.
    /// CMYK → RGB → Gray (BT.601).
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Sse41(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем статические маски
            var allFF = CmykSse41Vectors.AllFF;
            var shuffleC = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK = CmykSse41Vectors.ShuffleCmykK;

            var coefR = Gray8Sse41Vectors.CoefficientR_Q16;
            var coefG = Gray8Sse41Vectors.CoefficientG_Q16;
            var coefB = Gray8Sse41Vectors.CoefficientB_Q16;
            var half = Gray8Sse41Vectors.Half;

            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);

                var c = Ssse3.Shuffle(cmyk, shuffleC);
                var m = Ssse3.Shuffle(cmyk, shuffleM);
                var y = Ssse3.Shuffle(cmyk, shuffleY);
                var k = Ssse3.Shuffle(cmyk, shuffleK);

                var invC = Sse2.Subtract(allFF, c);
                var invM = Sse2.Subtract(allFF, m);
                var invY = Sse2.Subtract(allFF, y);
                var invK = Sse2.Subtract(allFF, k);

                var invCi = Sse41.ConvertToVector128Int32(invC);
                var invMi = Sse41.ConvertToVector128Int32(invM);
                var invYi = Sse41.ConvertToVector128Int32(invY);
                var invKi = Sse41.ConvertToVector128Int32(invK);

                var r = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invCi, invKi), 8);
                var g = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invMi, invKi), 8);
                var b = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invYi, invKi), 8);

                var gray = Sse41.MultiplyLow(r, coefR);
                gray = Sse2.Add(gray, Sse41.MultiplyLow(g, coefG));
                gray = Sse2.Add(gray, Sse41.MultiplyLow(b, coefB));
                gray = Sse2.Add(gray, half);
                gray = Sse2.ShiftRightLogical(gray, 16);

                var gray16 = Sse2.PackSignedSaturate(gray, gray);
                var gray8 = Sse2.PackUnsignedSaturate(gray16, gray16);

                *(uint*)dst = gray8.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = cmyk.ToGray8().Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Cmyk → Gray8)

    /// <summary>
    /// AVX2: Cmyk → Gray8.
    /// 8 пикселей за итерацию с настоящими 256-bit операциями.
    /// CMYK → RGB → BT.601 Gray.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Avx2(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // SSE константы для 4-pixel fallback (кешируем)
            var allFF = CmykSse41Vectors.AllFF;
            var shuffleC128 = CmykSse41Vectors.ShuffleCmykC;
            var shuffleM128 = CmykSse41Vectors.ShuffleCmykM;
            var shuffleY128 = CmykSse41Vectors.ShuffleCmykY;
            var shuffleK128 = CmykSse41Vectors.ShuffleCmykK;

            var coefR = Gray8Sse41Vectors.CoefficientR_Q16;
            var coefG = Gray8Sse41Vectors.CoefficientG_Q16;
            var coefB = Gray8Sse41Vectors.CoefficientB_Q16;
            var half = Gray8Sse41Vectors.Half;

            // AVX2 256-bit Q16 коэффициенты BT.601
            var coefR256 = Gray8Avx2Vectors.CoefficientR_Q16;
            var coefG256 = Gray8Avx2Vectors.CoefficientG_Q16;
            var coefB256 = Gray8Avx2Vectors.CoefficientB_Q16;
            var half256 = Gray8Avx2Vectors.Half_Q16;

            // === 8 пикселей за итерацию (32 байта CMYK → 8 байт Gray) ===
            // Обрабатываем 2×4 пикселя с AVX2 256-bit операциями
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
                var invC0_i32 = Sse41.ConvertToVector128Int32(invC0);
                var invC1_i32 = Sse41.ConvertToVector128Int32(invC1);
                var invC_256 = Vector256.Create(invC0_i32, invC1_i32);

                var invM0_i32 = Sse41.ConvertToVector128Int32(invM0);
                var invM1_i32 = Sse41.ConvertToVector128Int32(invM1);
                var invM_256 = Vector256.Create(invM0_i32, invM1_i32);

                var invY0_i32 = Sse41.ConvertToVector128Int32(invY0);
                var invY1_i32 = Sse41.ConvertToVector128Int32(invY1);
                var invY_256 = Vector256.Create(invY0_i32, invY1_i32);

                var invK0_i32 = Sse41.ConvertToVector128Int32(invK0);
                var invK1_i32 = Sse41.ConvertToVector128Int32(invK1);
                var invK_256 = Vector256.Create(invK0_i32, invK1_i32);

                // RGB = (255-C)*(255-K)/255 ≈ (255-C)*(255-K) >> 8
                var r256 = Avx2.ShiftRightLogical(Avx2.MultiplyLow(invC_256, invK_256), 8);
                var g256 = Avx2.ShiftRightLogical(Avx2.MultiplyLow(invM_256, invK_256), 8);
                var b256 = Avx2.ShiftRightLogical(Avx2.MultiplyLow(invY_256, invK_256), 8);

                // Gray = BT.601: 0.299*R + 0.587*G + 0.114*B (Q16)
                var gray256 = Avx2.MultiplyLow(r256, coefR256);
                gray256 = Avx2.Add(gray256, Avx2.MultiplyLow(g256, coefG256));
                gray256 = Avx2.Add(gray256, Avx2.MultiplyLow(b256, coefB256));
                gray256 = Avx2.Add(gray256, half256);
                gray256 = Avx2.ShiftRightLogical(gray256, 16);

                // Упаковка int32 → int16 → uint8
                var gray16_lo = Sse2.PackSignedSaturate(gray256.GetLower(), gray256.GetUpper());
                var gray8 = Sse2.PackUnsignedSaturate(gray16_lo, gray16_lo);

                *(ulong*)dst = gray8.AsUInt64().GetElement(0);

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

                var invC = Sse2.Subtract(allFF, c);
                var invM = Sse2.Subtract(allFF, m);
                var invY = Sse2.Subtract(allFF, y);
                var invK = Sse2.Subtract(allFF, k);

                var invCi = Sse41.ConvertToVector128Int32(invC);
                var invMi = Sse41.ConvertToVector128Int32(invM);
                var invYi = Sse41.ConvertToVector128Int32(invY);
                var invKi = Sse41.ConvertToVector128Int32(invK);

                var r = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invCi, invKi), 8);
                var g = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invMi, invKi), 8);
                var b = Sse2.ShiftRightLogical(Sse41.MultiplyLow(invYi, invKi), 8);

                var gray = Sse41.MultiplyLow(r, coefR);
                gray = Sse2.Add(gray, Sse41.MultiplyLow(g, coefG));
                gray = Sse2.Add(gray, Sse41.MultiplyLow(b, coefB));
                gray = Sse2.Add(gray, half);
                gray = Sse2.ShiftRightLogical(gray, 16);

                var gray16 = Sse2.PackSignedSaturate(gray, gray);
                var gray8 = Sse2.PackUnsignedSaturate(gray16, gray16);

                *(uint*)dst = gray8.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = cmyk.ToGray8().Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Cmyk)

    /// <summary>
    /// SSE41: Gray8 → Cmyk.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Sse41(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var allFF = CmykSse41Vectors.AllFF;
            var shuffle = CmykSse41Vectors.ShuffleGray8KToCmyk;

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var k = Sse2.Subtract(allFF, gray);

                var k0 = k;
                var k1 = Sse2.ShiftRightLogical128BitLane(k, 4);
                var k2 = Sse2.ShiftRightLogical128BitLane(k, 8);
                var k3 = Sse2.ShiftRightLogical128BitLane(k, 12);

                Ssse3.Shuffle(k0, shuffle).Store(dst);
                Ssse3.Shuffle(k1, shuffle).Store(dst + 16);
                Ssse3.Shuffle(k2, shuffle).Store(dst + 32);
                Ssse3.Shuffle(k3, shuffle).Store(dst + 48);

                src += 16;
                dst += 64;
                count -= 16;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Cmyk)

    /// <summary>
    /// AVX2: Gray8 → Cmyk.
    /// 32 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Avx2(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var allFF = CmykSse41Vectors.AllFF;
            var shuffle = CmykSse41Vectors.ShuffleGray8KToCmyk;

            while (count >= 32)
            {
                var gray = Avx.LoadVector256(src);
                var lo = gray.GetLower();
                var hi = gray.GetUpper();

                var kLo = Sse2.Subtract(allFF, lo);
                var kHi = Sse2.Subtract(allFF, hi);

                var k0 = kLo;
                var k1 = Sse2.ShiftRightLogical128BitLane(kLo, 4);
                var k2 = Sse2.ShiftRightLogical128BitLane(kLo, 8);
                var k3 = Sse2.ShiftRightLogical128BitLane(kLo, 12);

                Ssse3.Shuffle(k0, shuffle).Store(dst);
                Ssse3.Shuffle(k1, shuffle).Store(dst + 16);
                Ssse3.Shuffle(k2, shuffle).Store(dst + 32);
                Ssse3.Shuffle(k3, shuffle).Store(dst + 48);

                var k4 = kHi;
                var k5 = Sse2.ShiftRightLogical128BitLane(kHi, 4);
                var k6 = Sse2.ShiftRightLogical128BitLane(kHi, 8);
                var k7 = Sse2.ShiftRightLogical128BitLane(kHi, 12);

                Ssse3.Shuffle(k4, shuffle).Store(dst + 64);
                Ssse3.Shuffle(k5, shuffle).Store(dst + 80);
                Ssse3.Shuffle(k6, shuffle).Store(dst + 96);
                Ssse3.Shuffle(k7, shuffle).Store(dst + 112);

                src += 32;
                dst += 128;
                count -= 32;
            }

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var k = Sse2.Subtract(allFF, gray);

                var k0 = k;
                var k1 = Sse2.ShiftRightLogical128BitLane(k, 4);
                var k2 = Sse2.ShiftRightLogical128BitLane(k, 8);
                var k3 = Sse2.ShiftRightLogical128BitLane(k, 12);

                Ssse3.Shuffle(k0, shuffle).Store(dst);
                Ssse3.Shuffle(k1, shuffle).Store(dst + 16);
                Ssse3.Shuffle(k2, shuffle).Store(dst + 32);
                Ssse3.Shuffle(k3, shuffle).Store(dst + 48);

                src += 16;
                dst += 64;
                count -= 16;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Cmyk(Gray8 gray) => FromGray8(gray);
    public static explicit operator Gray8(Cmyk cmyk) => cmyk.ToGray8();

    #endregion
}
