#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S1854, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ YCbCr.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует YCbCr в Gray16 (Y масштабируется).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromYCbCr(YCbCr ycbcr) => new((ushort)(ycbcr.Y * 257));

    /// <summary>Конвертирует Gray16 в YCbCr с Q16 делением на 257.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr()
    {
        // Q16 деление на 257: (Value * 255 + 32768) >> 16 = lossless для V*257
        var y = (byte)(((Value * 255) + 32768) >> 16);
        return new(y, 128, 128);
    }

    #endregion

    #region Batch Conversion (Gray16 → YCbCr)

    /// <summary>Пакетная конвертация Gray16 → YCbCr.</summary>
    public static void ToYCbCr(ReadOnlySpan<Gray16> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → YCbCr с явным указанием ускорителя.</summary>
    public static unsafe void ToYCbCr(ReadOnlySpan<Gray16> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
                ToYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrCore(ReadOnlySpan<Gray16> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToYCbCrSse41(source, destination);
                break;
            default:
                ToYCbCrScalar(source, destination);
                break;
        }
    }

    private static unsafe void ToYCbCrParallel(Gray16* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToYCbCrCore(new ReadOnlySpan<Gray16>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (YCbCr → Gray16)

    /// <summary>Пакетная конвертация YCbCr → Gray16.</summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Gray16> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromYCbCrSse41(source, destination);
                break;
            default:
                FromYCbCrScalar(source, destination);
                break;
        }
    }

    private static unsafe void FromYCbCrParallel(YCbCr* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToYCbCr();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromYCbCr(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray16 → YCbCr)

    /// <summary>
    /// SSE41: Gray16 → YCbCr с Q16 делением на 257.
    /// 16 пикселей за итерацию (32 байта Gray16 → 48 байт YCbCr).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrSse41(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16
            var mult255 = Gray16Sse41Vectors.Mult255;

            // Маски для YCbCr (Y, 128, 128): аналогично Gray8.YCbCr
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            // Маски для установки Cb=128, Cr=128
            var yMask = Gray8Sse41Vectors.YMask;
            var cbcrMask = Gray8Sse41Vectors.CbCrMask;
            var yMask2 = Gray8Sse41Vectors.YMask2;
            var cbcrMask2 = Gray8Sse41Vectors.CbCrMask2;
            var yMask3 = Gray8Sse41Vectors.YMask3;
            var cbcrMask3 = Gray8Sse41Vectors.CbCrMask3;
            var yMask4 = Gray8Sse41Vectors.YMask4;
            var cbcrMask4 = Gray8Sse41Vectors.CbCrMask4;

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                // Загружаем 16 Gray16 (32 байта = 2 × Vector128)
                var g16_0 = Sse2.LoadVector128(src);
                var g16_1 = Sse2.LoadVector128(src + 8);

                // Q16 деление: (gray16 * 255 + 32768) >> 16 = hi + (lo >> 15)
                var lo0 = Sse2.MultiplyLow(g16_0, mult255);
                var hi0 = Sse2.MultiplyHigh(g16_0, mult255);
                var carry0 = Sse2.ShiftRightLogical(lo0, 15);
                var result0 = Sse2.Add(hi0, carry0);

                var lo1 = Sse2.MultiplyLow(g16_1, mult255);
                var hi1 = Sse2.MultiplyHigh(g16_1, mult255);
                var carry1 = Sse2.ShiftRightLogical(lo1, 15);
                var result1 = Sse2.Add(hi1, carry1);

                // Упаковываем 16 ushort → 16 байт
                var gray = Sse2.PackUnsignedSaturate(result0.AsInt16(), result1.AsInt16());

                // Первые 8 пикселей: gray[0..7] → 24 байта
                var rgb0 = Ssse3.Shuffle(gray, shuffle0);
                var rgb1 = Ssse3.Shuffle(gray, shuffle1);

                // Заменяем Y,Y,Y на Y,128,128
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));

                // Вторые 8 пикселей: gray[8..15] → 24 байта
                var hi = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(hi, shuffle2);
                var rgb3 = Ssse3.Shuffle(hi, shuffle3);

                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                // Объединяем средние части (rgb1 и rgb2)
                var out1 = Sse2.Or(rgb1, rgb2);

                // Записываем 48 байт
                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = (byte)(((*src * 255) + 32768) >> 16);
                *dst++ = v;
                *dst++ = 128;
                *dst++ = 128;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray16 → YCbCr)

    /// <summary>
    /// AVX2: Gray16 → YCbCr с Q16 делением на 257.
    /// 32 пикселя за итерацию (64 байта Gray16 → 96 байт YCbCr).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrAvx2(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16 = hi + (lo >> 15)
            var mult255 = Gray16Sse41Vectors.Mult255;

            // Маски для YCbCr
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            // Маски для YCbCr
            var yMask = Gray8Sse41Vectors.YMask;
            var cbcrMask = Gray8Sse41Vectors.CbCrMask;
            var yMask2 = Gray8Sse41Vectors.YMask2;
            var cbcrMask2 = Gray8Sse41Vectors.CbCrMask2;
            var yMask3 = Gray8Sse41Vectors.YMask3;
            var cbcrMask3 = Gray8Sse41Vectors.CbCrMask3;
            var yMask4 = Gray8Sse41Vectors.YMask4;
            var cbcrMask4 = Gray8Sse41Vectors.CbCrMask4;

            // 32 пикселя за итерацию (2 блока по 16)
            while (count >= 32)
            {
                // Первые 16 пикселей: Q16 деление
                var g16_0 = Sse2.LoadVector128(src);
                var g16_1 = Sse2.LoadVector128(src + 8);

                var lo0 = Sse2.MultiplyLow(g16_0, mult255);
                var hi0 = Sse2.MultiplyHigh(g16_0, mult255);
                var carry0 = Sse2.ShiftRightLogical(lo0, 15);
                var result0 = Sse2.Add(hi0, carry0);

                var lo1 = Sse2.MultiplyLow(g16_1, mult255);
                var hi1 = Sse2.MultiplyHigh(g16_1, mult255);
                var carry1 = Sse2.ShiftRightLogical(lo1, 15);
                var result1 = Sse2.Add(hi1, carry1);

                var gray0 = Sse2.PackUnsignedSaturate(result0.AsInt16(), result1.AsInt16());

                var rgb0_0 = Ssse3.Shuffle(gray0, shuffle0);
                var rgb1_0 = Ssse3.Shuffle(gray0, shuffle1);
                rgb0_0 = Sse2.Or(Sse2.And(rgb0_0, yMask), Sse2.AndNot(yMask, cbcrMask));
                rgb1_0 = Sse2.Or(Sse2.And(rgb1_0, yMask2), Sse2.AndNot(yMask2, cbcrMask2));

                var gray0_hi = Sse2.ShiftRightLogical128BitLane(gray0, 8);
                var rgb2_0 = Ssse3.Shuffle(gray0_hi, shuffle2);
                var rgb3_0 = Ssse3.Shuffle(gray0_hi, shuffle3);
                rgb2_0 = Sse2.Or(Sse2.And(rgb2_0, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                rgb3_0 = Sse2.Or(Sse2.And(rgb3_0, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                var out1_0 = Sse2.Or(rgb1_0, rgb2_0);

                rgb0_0.Store(dst);
                out1_0.Store(dst + 16);
                rgb3_0.Store(dst + 32);

                // Вторые 16 пикселей: Q16 деление
                var g16_2 = Sse2.LoadVector128(src + 16);
                var g16_3 = Sse2.LoadVector128(src + 24);

                var lo2 = Sse2.MultiplyLow(g16_2, mult255);
                var hi2 = Sse2.MultiplyHigh(g16_2, mult255);
                var carry2 = Sse2.ShiftRightLogical(lo2, 15);
                var result2 = Sse2.Add(hi2, carry2);

                var lo3 = Sse2.MultiplyLow(g16_3, mult255);
                var hi3 = Sse2.MultiplyHigh(g16_3, mult255);
                var carry3 = Sse2.ShiftRightLogical(lo3, 15);
                var result3 = Sse2.Add(hi3, carry3);

                var gray1 = Sse2.PackUnsignedSaturate(result2.AsInt16(), result3.AsInt16());

                var rgb0_1 = Ssse3.Shuffle(gray1, shuffle0);
                var rgb1_1 = Ssse3.Shuffle(gray1, shuffle1);
                rgb0_1 = Sse2.Or(Sse2.And(rgb0_1, yMask), Sse2.AndNot(yMask, cbcrMask));
                rgb1_1 = Sse2.Or(Sse2.And(rgb1_1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));

                var gray1_hi = Sse2.ShiftRightLogical128BitLane(gray1, 8);
                var rgb2_1 = Ssse3.Shuffle(gray1_hi, shuffle2);
                var rgb3_1 = Ssse3.Shuffle(gray1_hi, shuffle3);
                rgb2_1 = Sse2.Or(Sse2.And(rgb2_1, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                rgb3_1 = Sse2.Or(Sse2.And(rgb3_1, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                var out1_1 = Sse2.Or(rgb1_1, rgb2_1);

                rgb0_1.Store(dst + 48);
                out1_1.Store(dst + 64);
                rgb3_1.Store(dst + 80);

                src += 32;
                dst += 96;
                count -= 32;
            }

            // 16 пикселей (SSE fallback с Q16)
            while (count >= 16)
            {
                var g16_0 = Sse2.LoadVector128(src);
                var g16_1 = Sse2.LoadVector128(src + 8);

                var lo0 = Sse2.MultiplyLow(g16_0, mult255);
                var hi0 = Sse2.MultiplyHigh(g16_0, mult255);
                var carry0 = Sse2.ShiftRightLogical(lo0, 15);
                var result0 = Sse2.Add(hi0, carry0);

                var lo1 = Sse2.MultiplyLow(g16_1, mult255);
                var hi1 = Sse2.MultiplyHigh(g16_1, mult255);
                var carry1 = Sse2.ShiftRightLogical(lo1, 15);
                var result1 = Sse2.Add(hi1, carry1);

                var gray = Sse2.PackUnsignedSaturate(result0.AsInt16(), result1.AsInt16());

                var rgb0 = Ssse3.Shuffle(gray, shuffle0);
                var rgb1 = Ssse3.Shuffle(gray, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));

                var hi = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(hi, shuffle2);
                var rgb3 = Ssse3.Shuffle(hi, shuffle3);
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                var out1 = Sse2.Or(rgb1, rgb2);

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = (byte)(((*src * 255) + 32768) >> 16);
                *dst++ = v;
                *dst++ = 128;
                *dst++ = 128;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr → Gray16)

    /// <summary>
    /// SSE41: YCbCr → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Извлекаем Y из YCbCr (позиции 0, 3, 6, 9, 12, 15, 18, 21 в 24 байтах)
            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            var mult257 = Gray16Sse41Vectors.Mult257;

            while (count >= 8)
            {
                // Загружаем 24 байта (8 пикселей YCbCr)
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                // Извлекаем Y байты
                var y0 = Ssse3.Shuffle(v0, shuffleY0);
                var y1 = Ssse3.Shuffle(v1.AsByte(), shuffleY1);
                var yBytes = Sse2.Or(y0, y1);

                // Конвертируем byte → ushort и умножаем на 257
                var yLo = Sse41.ConvertToVector128Int16(yBytes);
                var y16 = Sse2.MultiplyLow(yLo, mult257);

                // Записываем 8 ushort
                Sse2.Store(dst, y16.AsUInt16());

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                var y = *src;
                *dst++ = (ushort)(y * 257);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr → Gray16)

    /// <summary>
    /// AVX2: YCbCr → Gray16.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            var mult257 = Gray16Sse41Vectors.Mult257;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var v0_0 = Sse2.LoadVector128(src);
                var v1_0 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                var y0_0 = Ssse3.Shuffle(v0_0, shuffleY0);
                var y1_0 = Ssse3.Shuffle(v1_0.AsByte(), shuffleY1);
                var yBytes0 = Sse2.Or(y0_0, y1_0);

                var yLo0 = Sse41.ConvertToVector128Int16(yBytes0);
                var y16_0 = Sse2.MultiplyLow(yLo0, mult257);

                Sse2.Store(dst, y16_0.AsUInt16());

                // Вторые 8 пикселей
                var v0_1 = Sse2.LoadVector128(src + 24);
                var v1_1 = Sse2.LoadScalarVector128((ulong*)(src + 40));

                var y0_1 = Ssse3.Shuffle(v0_1, shuffleY0);
                var y1_1 = Ssse3.Shuffle(v1_1.AsByte(), shuffleY1);
                var yBytes1 = Sse2.Or(y0_1, y1_1);

                var yLo1 = Sse41.ConvertToVector128Int16(yBytes1);
                var y16_1 = Sse2.MultiplyLow(yLo1, mult257);

                Sse2.Store(dst + 8, y16_1.AsUInt16());

                src += 48;
                dst += 16;
                count -= 16;
            }

            while (count >= 8)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                var y0 = Ssse3.Shuffle(v0, shuffleY0);
                var y1 = Ssse3.Shuffle(v1.AsByte(), shuffleY1);
                var yBytes = Sse2.Or(y0, y1);

                var yLo = Sse41.ConvertToVector128Int16(yBytes);
                var y16 = Sse2.MultiplyLow(yLo, mult257);

                Sse2.Store(dst, y16.AsUInt16());

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                var y = *src;
                *dst++ = (ushort)(y * 257);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray16(YCbCr ycbcr) => FromYCbCr(ycbcr);
    public static explicit operator YCbCr(Gray16 gray) => gray.ToYCbCr();

    #endregion
}
