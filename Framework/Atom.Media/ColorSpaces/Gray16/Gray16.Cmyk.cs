#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Cmyk.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    private const HardwareAcceleration CmykImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Cmyk в Gray16 через integer CMYK→RGB→Gray.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromCmyk(Cmyk cmyk)
    {
        // Точная integer формула: R = ((255-C)×(255-K) * 257 + 32768) >> 16 ≡ (x + 127) / 255
        var invK = 255 - cmyk.K;
        var r = (((255 - cmyk.C) * invK * 257) + 32768) >> 16;
        var g = (((255 - cmyk.M) * invK * 257) + 32768) >> 16;
        var b = (((255 - cmyk.Y) * invK * 257) + 32768) >> 16;

        // BT.601: Gray = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var gray8 = ((19595 * r) + (38470 * g) + (7471 * b) + 32768) >> 16;

        // Gray8 → Gray16: × 257
        return new((ushort)(gray8 * 257));
    }

    /// <summary>Конвертирует Gray16 в Cmyk с Q16 делением на 257.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk()
    {
        // Q16 деление на 257: (Value * 255 + 32768) >> 16 = lossless для V*257
        var value8 = (byte)(((Value * 255) + 32768) >> 16);
        return new(0, 0, 0, (byte)(255 - value8));
    }

    #endregion

    #region Batch Conversion (Gray16 → Cmyk)

    /// <summary>Пакетная конвертация Gray16 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Gray16> source, Span<Cmyk> destination) =>
        ToCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Cmyk с явным указанием ускорителя.</summary>
    public static unsafe void ToCmyk(ReadOnlySpan<Gray16> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, CmykImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
                ToCmykParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToCmykCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToCmykCore(ReadOnlySpan<Gray16> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToCmykAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToCmykSse41(source, destination);
                break;
            default:
                ToCmykScalar(source, destination);
                break;
        }
    }

    private static unsafe void ToCmykParallel(Gray16* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToCmykCore(new ReadOnlySpan<Gray16>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Cmyk → Gray16)

    /// <summary>Пакетная конвертация Cmyk → Gray16.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Gray16> destination) =>
        FromCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, CmykImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                FromCmykParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromCmykCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromCmykCore(ReadOnlySpan<Cmyk> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromCmykAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromCmykSse41(source, destination);
                break;
            default:
                FromCmykScalar(source, destination);
                break;
        }
    }

    private static unsafe void FromCmykParallel(Cmyk* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromCmykCore(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToCmykScalar(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToCmyk();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromCmykScalar(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromCmyk(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray16 → Cmyk)

    /// <summary>
    /// SSE41: Gray16 → Cmyk с Q16 делением на 257.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToCmykSse41(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16 = hi + (lo >> 15)
            var mult255 = Gray16Sse41Vectors.Mult255;
            var allFF = Gray16Sse41Vectors.AllFF;

            // CMYK layout: (0, 0, 0, K) где K = 255 - Gray
            var shuffle = Gray16Sse41Vectors.ShuffleGray8ToCmyk;

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);

                // Q16: (gray16 * 255 + 32768) >> 16 = hi + (lo >> 15)
                var lo = Sse2.MultiplyLow(gray16, mult255);
                var hi = Sse2.MultiplyHigh(gray16, mult255);
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);

                // Упаковываем 8 ushort → 8 байт
                var gray8 = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());

                // K = 255 - Gray
                var k = Sse2.Subtract(allFF, gray8);

                // Расширяем 8 байт в 8 × 4 байта CMYK
                var k0 = k;
                var k1 = Sse2.ShiftRightLogical128BitLane(k, 4);

                var cmyk0 = Ssse3.Shuffle(k0, shuffle);
                var cmyk1 = Ssse3.Shuffle(k1, shuffle);

                Sse2.Store(dst, cmyk0);
                Sse2.Store(dst + 16, cmyk1);

                src += 8;
                dst += 32;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(((*src * 255) + 32768) >> 16);
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
    /// AVX2: Gray16 → Cmyk с Q16 делением на 257.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToCmykAvx2(ReadOnlySpan<Gray16> source, Span<Cmyk> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16 = hi + (lo >> 15)
            var mult255 = Gray16Sse41Vectors.Mult255;
            var allFF = Gray16Sse41Vectors.AllFF;

            var shuffle = Gray16Sse41Vectors.ShuffleGray8ToCmyk;

            while (count >= 16)
            {
                // Первые 8 пикселей: Q16 деление
                var gray16_0 = Sse2.LoadVector128(src);

                var lo0 = Sse2.MultiplyLow(gray16_0, mult255);
                var hi0 = Sse2.MultiplyHigh(gray16_0, mult255);
                var carry0 = Sse2.ShiftRightLogical(lo0, 15);
                var result0 = Sse2.Add(hi0, carry0);
                var gray8_0 = Sse2.PackUnsignedSaturate(result0.AsInt16(), result0.AsInt16());

                var k0 = Sse2.Subtract(allFF, gray8_0);

                var k0_0 = k0;
                var k0_1 = Sse2.ShiftRightLogical128BitLane(k0, 4);

                Ssse3.Shuffle(k0_0, shuffle).Store(dst);
                Ssse3.Shuffle(k0_1, shuffle).Store(dst + 16);

                // Вторые 8 пикселей: Q16 деление
                var gray16_1 = Sse2.LoadVector128(src + 8);

                var lo1 = Sse2.MultiplyLow(gray16_1, mult255);
                var hi1 = Sse2.MultiplyHigh(gray16_1, mult255);
                var carry1 = Sse2.ShiftRightLogical(lo1, 15);
                var result1 = Sse2.Add(hi1, carry1);
                var gray8_1 = Sse2.PackUnsignedSaturate(result1.AsInt16(), result1.AsInt16());

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

                var lo = Sse2.MultiplyLow(gray16, mult255);
                var hi = Sse2.MultiplyHigh(gray16, mult255);
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);
                var gray8 = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());

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
                var v = (byte)(((*src * 255) + 32768) >> 16);
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

    #region SSE41 Implementation (Cmyk → Gray16)

    /// <summary>
    /// SSE41: Cmyk → Gray16.
    /// 4 пикселя за итерацию (integer арифметика).
    /// CMYK → RGB: R = (255-C)×(255-K) >> 8
    /// RGB → Gray: BT.601 Q16
    /// Gray8 → Gray16: × 257
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromCmykSse41(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Статические маски из Gray16Sse41Vectors
            var shuffleC = Gray16Sse41Vectors.ShuffleCmykToC;
            var shuffleM = Gray16Sse41Vectors.ShuffleCmykToM;
            var shuffleY = Gray16Sse41Vectors.ShuffleCmykToY;
            var shuffleK = Gray16Sse41Vectors.ShuffleCmykToK;

            // Integer константы
            var allFF = Gray16Sse41Vectors.AllFF;

            // BT.601 Q16 коэффициенты из Gray16Sse41Vectors
            var coefR = Gray16Sse41Vectors.CoefficientR;
            var coefG = Gray16Sse41Vectors.CoefficientG;
            var coefB = Gray16Sse41Vectors.CoefficientB;
            var half32768 = Gray16Sse41Vectors.Half;
            var mult257 = Gray16Sse41Vectors.Scale8To16;

            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);

                // Извлекаем каналы C, M, Y, K
                var c = Ssse3.Shuffle(cmyk, shuffleC);
                var m = Ssse3.Shuffle(cmyk, shuffleM);
                var y = Ssse3.Shuffle(cmyk, shuffleY);
                var k = Ssse3.Shuffle(cmyk, shuffleK);

                // 255 - C/M/Y/K
                var invC = Sse2.Subtract(allFF, c);
                var invM = Sse2.Subtract(allFF, m);
                var invY = Sse2.Subtract(allFF, y);
                var invK = Sse2.Subtract(allFF, k);

                // Расширяем до int32
                var invCi = Sse41.ConvertToVector128Int32(invC);
                var invMi = Sse41.ConvertToVector128Int32(invM);
                var invYi = Sse41.ConvertToVector128Int32(invY);
                var invKi = Sse41.ConvertToVector128Int32(invK);

                // R = ((255-C)×(255-K) * 257 + 32768) >> 16 (точное деление на 255 с округлением)
                var productC = Sse41.MultiplyLow(invCi, invKi);
                var productM = Sse41.MultiplyLow(invMi, invKi);
                var productY = Sse41.MultiplyLow(invYi, invKi);

                var rI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productC, mult257), half32768), 16);
                var gI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productM, mult257), half32768), 16);
                var bI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productY, mult257), half32768), 16);

                // Gray = (19595*R + 38470*G + 7471*B + 32768) >> 16
                var gray8 = Sse41.MultiplyLow(rI, coefR);
                gray8 = Sse2.Add(gray8, Sse41.MultiplyLow(gI, coefG));
                gray8 = Sse2.Add(gray8, Sse41.MultiplyLow(bI, coefB));
                gray8 = Sse2.Add(gray8, half32768);
                gray8 = Sse2.ShiftRightLogical(gray8, 16);

                // Gray8 → Gray16: × 257
                var gray16 = Sse41.MultiplyLow(gray8, mult257);

                // Pack int32 → uint16
                var packed = Sse41.PackUnsignedSaturate(gray16, gray16);

                *(ulong*)dst = packed.AsUInt64().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = FromCmyk(cmyk).Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Cmyk → Gray16)

    /// <summary>
    /// AVX2: Cmyk → Gray16.
    /// 8 пикселей за итерацию с настоящим AVX2 256-bit integer.
    /// CMYK → RGB: (255-C)×(255-K) >> 8
    /// RGB → Gray: BT.601 Q16
    /// Gray8 → Gray16: × 257
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromCmykAvx2(ReadOnlySpan<Cmyk> source, Span<Gray16> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Статические маски из Gray16Sse41Vectors (кешируем в регистрах)
            var shuffleC = Gray16Sse41Vectors.ShuffleCmykToC;
            var shuffleM = Gray16Sse41Vectors.ShuffleCmykToM;
            var shuffleY = Gray16Sse41Vectors.ShuffleCmykToY;
            var shuffleK = Gray16Sse41Vectors.ShuffleCmykToK;

            // Integer константы
            var allFF = Gray16Sse41Vectors.AllFF;

            // Q16 коэффициенты для AVX2 (кешируем в регистрах)
            var coefR256 = Gray16Avx2Vectors.CoefficientR;
            var coefG256 = Gray16Avx2Vectors.CoefficientG;
            var coefB256 = Gray16Avx2Vectors.CoefficientB;
            var half32768_256 = Gray16Avx2Vectors.Half;
            var mult257 = Gray16Avx2Vectors.Scale8To16;

            // Q16 коэффициенты для SSE fallback
            var coefR = Gray16Sse41Vectors.CoefficientR;
            var coefG = Gray16Sse41Vectors.CoefficientG;
            var coefB = Gray16Sse41Vectors.CoefficientB;
            var half32768 = Gray16Sse41Vectors.Half;
            var mult257_128 = Gray16Sse41Vectors.Scale8To16;

            while (count >= 8)
            {
                // Загрузка 8 пикселей CMYK = 32 байта
                var cmyk0 = Sse2.LoadVector128(src);       // Пиксели 0-3
                var cmyk1 = Sse2.LoadVector128(src + 16);  // Пиксели 4-7

                // Деинтерливинг CMYK → C, M, Y, K (по 4 байта каждый)
                var c0 = Ssse3.Shuffle(cmyk0, shuffleC);
                var m0 = Ssse3.Shuffle(cmyk0, shuffleM);
                var y0 = Ssse3.Shuffle(cmyk0, shuffleY);
                var k0 = Ssse3.Shuffle(cmyk0, shuffleK);

                var c1 = Ssse3.Shuffle(cmyk1, shuffleC);
                var m1 = Ssse3.Shuffle(cmyk1, shuffleM);
                var y1 = Ssse3.Shuffle(cmyk1, shuffleY);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK);

                // 255 - C/M/Y/K
                var invC0 = Sse2.Subtract(allFF, c0);
                var invM0 = Sse2.Subtract(allFF, m0);
                var invY0 = Sse2.Subtract(allFF, y0);
                var invK0 = Sse2.Subtract(allFF, k0);

                var invC1 = Sse2.Subtract(allFF, c1);
                var invM1 = Sse2.Subtract(allFF, m1);
                var invY1 = Sse2.Subtract(allFF, y1);
                var invK1 = Sse2.Subtract(allFF, k1);

                // Расширяем до int32 и объединяем в 256-bit вектора (8 пикселей)
                var invCi256 = Vector256.Create(Sse41.ConvertToVector128Int32(invC0), Sse41.ConvertToVector128Int32(invC1));
                var invMi256 = Vector256.Create(Sse41.ConvertToVector128Int32(invM0), Sse41.ConvertToVector128Int32(invM1));
                var invYi256 = Vector256.Create(Sse41.ConvertToVector128Int32(invY0), Sse41.ConvertToVector128Int32(invY1));
                var invKi256 = Vector256.Create(Sse41.ConvertToVector128Int32(invK0), Sse41.ConvertToVector128Int32(invK1));

                // R = ((255-C)×(255-K) * 257 + 32768) >> 16 (точное деление на 255)
                var productC = Avx2.MultiplyLow(invCi256, invKi256);
                var productM = Avx2.MultiplyLow(invMi256, invKi256);
                var productY = Avx2.MultiplyLow(invYi256, invKi256);

                var rI = Avx2.ShiftRightLogical(Avx2.Add(Avx2.MultiplyLow(productC, mult257), half32768_256), 16);
                var gI = Avx2.ShiftRightLogical(Avx2.Add(Avx2.MultiplyLow(productM, mult257), half32768_256), 16);
                var bI = Avx2.ShiftRightLogical(Avx2.Add(Avx2.MultiplyLow(productY, mult257), half32768_256), 16);

                // Gray = (19595*R + 38470*G + 7471*B + 32768) >> 16 (Q16 как в SSE41)
                var gray8 = Avx2.MultiplyLow(rI, coefR256);
                gray8 = Avx2.Add(gray8, Avx2.MultiplyLow(gI, coefG256));
                gray8 = Avx2.Add(gray8, Avx2.MultiplyLow(bI, coefB256));
                gray8 = Avx2.Add(gray8, half32768_256);
                gray8 = Avx2.ShiftRightLogical(gray8, 16);

                // Gray8 → Gray16: × 257
                var gray16 = Avx2.MultiplyLow(gray8, mult257);

                // Pack int32 → uint16 (8 значений)
                // PackUnsignedSaturate работает in-lane, нужен permute
                var packed = Avx2.PackUnsignedSaturate(gray16, gray16);
                packed = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsUInt16();

                // Записываем 8 значений Gray16 (16 байт)
                Sse2.Store(dst, packed.GetLower());

                src += 32;
                dst += 8;
                count -= 8;
            }

            // Остаток: 4 пикселя (SSE fallback)
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

                var productC = Sse41.MultiplyLow(invCi, invKi);
                var productM = Sse41.MultiplyLow(invMi, invKi);
                var productY = Sse41.MultiplyLow(invYi, invKi);

                var rI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productC, mult257_128), half32768), 16);
                var gI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productM, mult257_128), half32768), 16);
                var bI = Sse2.ShiftRightLogical(Sse2.Add(Sse41.MultiplyLow(productY, mult257_128), half32768), 16);

                var gray8 = Sse41.MultiplyLow(rI, coefR);
                gray8 = Sse2.Add(gray8, Sse41.MultiplyLow(gI, coefG));
                gray8 = Sse2.Add(gray8, Sse41.MultiplyLow(bI, coefB));
                gray8 = Sse2.Add(gray8, half32768);
                gray8 = Sse2.ShiftRightLogical(gray8, 16);

                var gray16 = Sse41.MultiplyLow(gray8, mult257_128);
                var packed = Sse41.PackUnsignedSaturate(gray16, gray16);

                *(ulong*)dst = packed.AsUInt64().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                var cmyk = *(Cmyk*)src;
                *dst++ = FromCmyk(cmyk).Value;
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray16(Cmyk cmyk) => FromCmyk(cmyk);
    public static explicit operator Cmyk(Gray16 gray) => gray.ToCmyk();

    #endregion
}
