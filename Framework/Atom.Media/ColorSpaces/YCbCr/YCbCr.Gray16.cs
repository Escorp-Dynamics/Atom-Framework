#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCbCr ↔ Gray16.
/// </summary>
public readonly partial struct YCbCr
{
    #region SIMD Constants

    private const HardwareAcceleration Gray16Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в YCbCr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCbCr FromGray16(Gray16 gray)
    {
        // Сначала в Gray8, потом в YCbCr
        var y = (byte)(gray.Value >> 8);
        return new(y, 128, 128);
    }

    /// <summary>Конвертирует YCbCr в Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16()
    {
        // Y: 0-255 → 0-65535 (умножаем на 257 для точного маппинга)
        return new((ushort)(Y * 257));
    }

    #endregion

    #region Batch Conversion (YCbCr → Gray16)

    /// <summary>Пакетная конвертация YCbCr → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<YCbCr> source, Span<Gray16> destination) =>
        ToGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray16(ReadOnlySpan<YCbCr> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                ToGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray16Core(ReadOnlySpan<YCbCr> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToGray16Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToGray16Sse41(source, destination);
                break;
            default:
                ToGray16Scalar(source, destination);
                break;
        }
    }

    private static unsafe void ToGray16Parallel(YCbCr* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray16Core(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray16 → YCbCr)

    /// <summary>Пакетная конвертация Gray16 → YCbCr.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<YCbCr> destination) =>
        FromGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → YCbCr с явным указанием ускорителя.</summary>
    public static unsafe void FromGray16(ReadOnlySpan<Gray16> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
                FromGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray16Core(ReadOnlySpan<Gray16> source, Span<YCbCr> destination, HardwareAcceleration selected)
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

    private static unsafe void FromGray16Parallel(Gray16* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<YCbCr>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray16Core(new ReadOnlySpan<Gray16>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray16Scalar(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
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
    private static unsafe void FromGray16Scalar(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray16(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr → Gray16)

    /// <summary>
    /// SSE41: YCbCr → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Sse41(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Извлекаем Y из YCbCr (позиции 0, 3, 6, 9, 12, 15, 18, 21)
            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            var mult257 = YCbCrSse41Vectors.Mult257;

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

    #region AVX2 Implementation (YCbCr → Gray16)

    /// <summary>
    /// AVX2: YCbCr → Gray16.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Avx2(ReadOnlySpan<YCbCr> source, Span<Gray16> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            var mult257 = YCbCrSse41Vectors.Mult257;

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

    #region SSE41 Implementation (Gray16 → YCbCr)

    /// <summary>
    /// SSE41: Gray16 → YCbCr.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Sse41(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleHigh = YCbCrSse41Vectors.ShuffleHighBytes;

            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;

            var cbcrMask = YCbCrSse41Vectors.CbCrMask128;
            var cbcrMask2 = YCbCrSse41Vectors.CbCrMask128_Alt;

            // Маски Y для применения And — кешируем вне цикла
            var yMask = YCbCrSse41Vectors.YMask128;
            var yMaskAlt = YCbCrSse41Vectors.YMask128_Alt;

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);

                var v0 = Ssse3.Shuffle(gray8, shuffle0);
                var v1 = Ssse3.Shuffle(gray8, shuffle1);

                v0 = Sse2.Or(Sse2.And(v0, yMask), cbcrMask);
                v1 = Sse2.Or(Sse2.And(v1, yMaskAlt), cbcrMask2);

                Sse2.Store(dst, v0);
                Unsafe.WriteUnaligned(dst + 16, v1.AsUInt64().GetElement(0));

                src += 8;
                dst += 24;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
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
    /// AVX2: Gray16 → YCbCr.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Avx2(ReadOnlySpan<Gray16> source, Span<YCbCr> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleHigh = YCbCrSse41Vectors.ShuffleHighBytes;

            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;

            var cbcrMask = YCbCrSse41Vectors.CbCrMask128;
            var cbcrMask2 = YCbCrSse41Vectors.CbCrMask128_Alt;

            // Маски Y для применения And — кешируем вне цикла
            var yMask = YCbCrSse41Vectors.YMask128;
            var yMaskAlt = YCbCrSse41Vectors.YMask128_Alt;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var gray16_0 = Sse2.LoadVector128(src);
                var gray8_0 = Ssse3.Shuffle(gray16_0.AsByte(), shuffleHigh);

                var v0_0 = Ssse3.Shuffle(gray8_0, shuffle0);
                var v1_0 = Ssse3.Shuffle(gray8_0, shuffle1);

                v0_0 = Sse2.Or(Sse2.And(v0_0, yMask), cbcrMask);
                v1_0 = Sse2.Or(Sse2.And(v1_0, yMaskAlt), cbcrMask2);

                Sse2.Store(dst, v0_0);
                Unsafe.WriteUnaligned(dst + 16, v1_0.AsUInt64().GetElement(0));

                // Вторые 8 пикселей
                var gray16_1 = Sse2.LoadVector128(src + 8);
                var gray8_1 = Ssse3.Shuffle(gray16_1.AsByte(), shuffleHigh);

                var v0_1 = Ssse3.Shuffle(gray8_1, shuffle0);
                var v1_1 = Ssse3.Shuffle(gray8_1, shuffle1);

                v0_1 = Sse2.Or(Sse2.And(v0_1, yMask), cbcrMask);
                v1_1 = Sse2.Or(Sse2.And(v1_1, yMaskAlt), cbcrMask2);

                Sse2.Store(dst + 24, v0_1);
                Unsafe.WriteUnaligned(dst + 40, v1_1.AsUInt64().GetElement(0));

                src += 16;
                dst += 48;
                count -= 16;
            }

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);

                var v0 = Ssse3.Shuffle(gray8, shuffle0);
                var v1 = Ssse3.Shuffle(gray8, shuffle1);

                v0 = Sse2.Or(Sse2.And(v0, yMask), cbcrMask);
                v1 = Sse2.Or(Sse2.And(v1, yMaskAlt), cbcrMask2);

                Sse2.Store(dst, v0);
                Unsafe.WriteUnaligned(dst + 16, v1.AsUInt64().GetElement(0));

                src += 8;
                dst += 24;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
                *dst++ = v;
                *dst++ = 128;
                *dst++ = 128;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator YCbCr(Gray16 gray) => FromGray16(gray);
    public static explicit operator Gray16(YCbCr ycbcr) => ycbcr.ToGray16();

    #endregion
}
