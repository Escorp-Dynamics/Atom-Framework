#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Gray8.
/// </summary>
public readonly partial struct YCbCr
{
    #region SIMD Constants

    private const HardwareAcceleration Gray8Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в YCbCr (Y = Value, Cb = Cr = 128).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromGray8(Gray8 gray) => new(gray.Value, 128, 128);

    /// <summary>Конвертирует YCbCr в Gray8 (берём только Y).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => new(Y);

    #endregion

    #region Batch Conversion (YCbCr → Gray8)

    /// <summary>Пакетная конвертация YCbCr → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<YCbCr> source, Span<Gray8> destination) =>
        ToGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray8(ReadOnlySpan<YCbCr> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                ToGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray8Core(ReadOnlySpan<YCbCr> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToGray8Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToGray8Sse41(source, destination);
                break;
            default:
                ToGray8Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToGray8Parallel(YCbCr* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray8Core(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray8 → YCbCr)

    /// <summary>Пакетная конвертация Gray8 → YCbCr.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<YCbCr> destination) =>
        FromGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → YCbCr с явным указанием ускорителя.</summary>
    public static unsafe void FromGray8(ReadOnlySpan<Gray8> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
                FromGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray8Core(ReadOnlySpan<Gray8> source, Span<YCbCr> destination, HardwareAcceleration selected)
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

    private static unsafe void FromGray8Parallel(Gray8* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray8Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray8Scalar(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
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
    private static unsafe void FromGray8Scalar(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray8(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr → Gray8)

    /// <summary>
    /// SSE41: YCbCr → Gray8.
    /// Извлекает Y из каждого триплета.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Sse41(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            while (count >= 8)
            {
                var bytes0 = Sse2.LoadVector128(src);
                var bytes1 = Sse2.LoadScalarVector128((long*)(src + 16));

                var y0 = Ssse3.Shuffle(bytes0, shuffleY0);
                var y1 = Ssse3.Shuffle(bytes1.AsByte(), shuffleY1);
                var y = Sse2.Or(y0, y1);

                Unsafe.WriteUnaligned(dst, y.AsUInt64().GetElement(0));

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                *dst++ = *src;
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr → Gray8)

    /// <summary>
    /// AVX2: YCbCr → Gray8.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Avx2(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var bytes0a = Sse2.LoadVector128(src);
                var bytes1a = Sse2.LoadScalarVector128((long*)(src + 16));
                var y0a = Ssse3.Shuffle(bytes0a, shuffleY0);
                var y1a = Ssse3.Shuffle(bytes1a.AsByte(), shuffleY1);
                var ya = Sse2.Or(y0a, y1a);

                // Вторые 8 пикселей
                var bytes0b = Sse2.LoadVector128(src + 24);
                var bytes1b = Sse2.LoadScalarVector128((long*)(src + 40));
                var y0b = Ssse3.Shuffle(bytes0b, shuffleY0);
                var y1b = Ssse3.Shuffle(bytes1b.AsByte(), shuffleY1);
                var yb = Sse2.Or(y0b, y1b);

                var result = Sse2.UnpackLow(ya.AsUInt64(), yb.AsUInt64()).AsByte();

                result.Store(dst);

                src += 48;
                dst += 16;
                count -= 16;
            }

            while (count >= 8)
            {
                var bytes0 = Sse2.LoadVector128(src);
                var bytes1 = Sse2.LoadScalarVector128((long*)(src + 16));
                var y0 = Ssse3.Shuffle(bytes0, shuffleY0);
                var y1 = Ssse3.Shuffle(bytes1.AsByte(), shuffleY1);
                var y = Sse2.Or(y0, y1);

                Unsafe.WriteUnaligned(dst, y.AsUInt64().GetElement(0));

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                *dst++ = *src;
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → YCbCr)

    /// <summary>
    /// SSE41: Gray8 → YCbCr.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Sse41(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            var cbcrMask = YCbCrSse41Vectors.CbCrMask128;
            var yMask = YCbCrSse41Vectors.YMask128;

            var cbcrMask2 = YCbCrSse41Vectors.CbCrMask128_2;
            var yMask2 = YCbCrSse41Vectors.YMask128_2;

            var cbcrMask3 = YCbCrSse41Vectors.CbCrMask128_3;
            var yMask3 = YCbCrSse41Vectors.YMask128_3;

            var cbcrMask4 = YCbCrSse41Vectors.CbCrMask128_4;
            var yMask4 = YCbCrSse41Vectors.YMask128_4;

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var lo = gray;
                var rgb0 = Ssse3.Shuffle(lo, shuffle0);
                var rgb1 = Ssse3.Shuffle(lo, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                var hi = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(hi, shuffle2);
                var rgb3 = Ssse3.Shuffle(hi, shuffle3);
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out1 = Sse2.Or(rgb1, rgb2);
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = 128;
                *dst++ = 128;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → YCbCr)

    /// <summary>
    /// AVX2: Gray8 → YCbCr.
    /// 32 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Avx2(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            var cbcrMask = YCbCrSse41Vectors.CbCrMask128;
            var yMask = YCbCrSse41Vectors.YMask128;

            var cbcrMask2 = YCbCrSse41Vectors.CbCrMask128_2;
            var yMask2 = YCbCrSse41Vectors.YMask128_2;

            var cbcrMask3 = YCbCrSse41Vectors.CbCrMask128_3;
            var yMask3 = YCbCrSse41Vectors.YMask128_3;

            var cbcrMask4 = YCbCrSse41Vectors.CbCrMask128_4;
            var yMask4 = YCbCrSse41Vectors.YMask128_4;

            while (count >= 32)
            {
                var gray = Avx.LoadVector256(src);
                var lo = gray.GetLower();
                var hi = gray.GetUpper();

                // Первые 16 пикселей
                var lo0 = lo;
                var rgb0 = Ssse3.Shuffle(lo0, shuffle0);
                var rgb1 = Ssse3.Shuffle(lo0, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                var lo1 = Sse2.ShiftRightLogical128BitLane(lo, 8);
                var rgb2 = Ssse3.Shuffle(lo1, shuffle2);
                var rgb3 = Ssse3.Shuffle(lo1, shuffle3);
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out1 = Sse2.Or(rgb1, rgb2);
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                // Вторые 16 пикселей
                var hi0 = hi;
                var rgb4 = Ssse3.Shuffle(hi0, shuffle0);
                var rgb5 = Ssse3.Shuffle(hi0, shuffle1);
                rgb4 = Sse2.Or(Sse2.And(rgb4, yMask), Sse2.AndNot(yMask, cbcrMask));

                var hi1 = Sse2.ShiftRightLogical128BitLane(hi, 8);
                var rgb6 = Ssse3.Shuffle(hi1, shuffle2);
                var rgb7 = Ssse3.Shuffle(hi1, shuffle3);
                rgb5 = Sse2.Or(Sse2.And(rgb5, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb6 = Sse2.Or(Sse2.And(rgb6, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out2 = Sse2.Or(rgb5, rgb6);
                rgb7 = Sse2.Or(Sse2.And(rgb7, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb4.Store(dst + 48);
                out2.Store(dst + 64);
                rgb7.Store(dst + 80);

                src += 32;
                dst += 96;
                count -= 32;
            }

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var lo0 = gray;
                var rgb0 = Ssse3.Shuffle(lo0, shuffle0);
                var rgb1 = Ssse3.Shuffle(lo0, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                var lo1 = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(lo1, shuffle2);
                var rgb3 = Ssse3.Shuffle(lo1, shuffle3);
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out1 = Sse2.Or(rgb1, rgb2);
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;
                *dst++ = 128;
                *dst++ = 128;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator YCbCr(Gray8 gray) => FromGray8(gray);
    public static explicit operator Gray8(YCbCr ycbcr) => ycbcr.ToGray8();

    #endregion
}
