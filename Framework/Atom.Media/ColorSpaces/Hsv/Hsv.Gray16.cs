#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S1854, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Gray16.
/// </summary>
public readonly partial struct Hsv
{
    #region SIMD Constants

    // ВРЕМЕННО: SIMD отключено — HSV алгоритм обновлён на 4-байтовый формат, SIMD требует переработки.
    private const HardwareAcceleration Gray16Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray16 в Hsv (H = 0, S = 0, V масштабируется).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromGray16(Gray16 gray)
    {
        // Value: 0-65535 → 0-255
        var v = (byte)(gray.Value >> 8);
        return new(0, 0, v);
    }

    /// <summary>Конвертирует Hsv в Gray16 (V масштабируется).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16()
    {
        // V: 0-255 → 0-65535 (умножаем на 257 для точного маппинга)
        return new((ushort)(V * 257));
    }

    #endregion

    #region Batch Conversion (Hsv → Gray16)

    /// <summary>Пакетная конвертация Hsv → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<Hsv> source, Span<Gray16> destination) =>
        ToGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray16(ReadOnlySpan<Hsv> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Hsv* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                ToGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray16Core(ReadOnlySpan<Hsv> source, Span<Gray16> destination, HardwareAcceleration selected)
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

    private static unsafe void ToGray16Parallel(Hsv* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Hsv>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray16Core(new ReadOnlySpan<Hsv>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray16 → Hsv)

    /// <summary>Пакетная конвертация Gray16 → Hsv.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<Hsv> destination) =>
        FromGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Hsv с явным указанием ускорителя.</summary>
    public static unsafe void FromGray16(ReadOnlySpan<Gray16> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Hsv* dstPtr = destination)
                FromGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray16Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray16Core(ReadOnlySpan<Gray16> source, Span<Hsv> destination, HardwareAcceleration selected)
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

    private static unsafe void FromGray16Parallel(Gray16* source, Hsv* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Hsv>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray16Core(new ReadOnlySpan<Gray16>(source + start, size), new Span<Hsv>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray16Scalar(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
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
    private static unsafe void FromGray16Scalar(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray16(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Hsv → Gray16)

    /// <summary>
    /// SSE41: Hsv → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Sse41(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Кешируем маски в локальные переменные
            var shuffleV = HsvSse41Vectors.ShuffleHsvToV;
            var shuffleV2 = HsvSse41Vectors.ShuffleHsvToV2;
            var mult257 = Gray16Sse41Vectors.Mult257;

            while (count >= 8)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                var vBytes0 = Ssse3.Shuffle(v0, shuffleV);
                var vBytes1 = Ssse3.Shuffle(v1.AsByte(), shuffleV2);
                var vBytes = Sse2.Or(vBytes0, vBytes1);

                var vLo = Sse41.ConvertToVector128Int16(vBytes);
                var v16 = Sse2.MultiplyLow(vLo, mult257);

                Sse2.Store(dst, v16.AsUInt16());

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                var v = src[2];
                *dst++ = (ushort)(v * 257);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Hsv → Gray16)

    /// <summary>
    /// AVX2: Hsv → Gray16.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Avx2(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // Кешируем маски в локальные переменные
            var shuffleV = HsvSse41Vectors.ShuffleHsvToV;
            var shuffleV2 = HsvSse41Vectors.ShuffleHsvToV2;
            var mult257 = Gray16Sse41Vectors.Mult257;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var v0_0 = Sse2.LoadVector128(src);
                var v1_0 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                var vBytes0_0 = Ssse3.Shuffle(v0_0, shuffleV);
                var vBytes1_0 = Ssse3.Shuffle(v1_0.AsByte(), shuffleV2);
                var vBytes_0 = Sse2.Or(vBytes0_0, vBytes1_0);

                var vLo_0 = Sse41.ConvertToVector128Int16(vBytes_0);
                var v16_0 = Sse2.MultiplyLow(vLo_0, mult257);

                Sse2.Store(dst, v16_0.AsUInt16());

                // Вторые 8 пикселей
                var v0_1 = Sse2.LoadVector128(src + 24);
                var v1_1 = Sse2.LoadScalarVector128((ulong*)(src + 40));

                var vBytes0_1 = Ssse3.Shuffle(v0_1, shuffleV);
                var vBytes1_1 = Ssse3.Shuffle(v1_1.AsByte(), shuffleV2);
                var vBytes_1 = Sse2.Or(vBytes0_1, vBytes1_1);

                var vLo_1 = Sse41.ConvertToVector128Int16(vBytes_1);
                var v16_1 = Sse2.MultiplyLow(vLo_1, mult257);

                Sse2.Store(dst + 8, v16_1.AsUInt16());

                src += 48;
                dst += 16;
                count -= 16;
            }

            while (count >= 8)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadScalarVector128((ulong*)(src + 16));

                var vBytes0 = Ssse3.Shuffle(v0, shuffleV);
                var vBytes1 = Ssse3.Shuffle(v1.AsByte(), shuffleV2);
                var vBytes = Sse2.Or(vBytes0, vBytes1);

                var vLo = Sse41.ConvertToVector128Int16(vBytes);
                var v16 = Sse2.MultiplyLow(vLo, mult257);

                Sse2.Store(dst, v16.AsUInt16());

                src += 24;
                dst += 8;
                count -= 8;
            }

            while (count > 0)
            {
                var v = src[2];
                *dst++ = (ushort)(v * 257);
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray16 → Hsv)

    /// <summary>
    /// SSE41: Gray16 → Hsv.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Sse41(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем маски в локальные переменные
            var shuffleHigh = HsvSse41Vectors.ShuffleGray16ToHighByte;
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var hsMask0 = HsvSse41Vectors.HsvGrayMask0;
            var hsMask1 = HsvSse41Vectors.HsvGrayMask1;

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);

                var v0 = Sse2.And(Ssse3.Shuffle(gray8, shuffle0), hsMask0);
                var v1 = Sse2.And(Ssse3.Shuffle(gray8, shuffle1), hsMask1);

                Sse2.Store(dst, v0);
                Sse2.StoreLow((double*)(dst + 16), v1.AsDouble());

                src += 8;
                dst += 24;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = v;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray16 → Hsv)

    /// <summary>
    /// AVX2: Gray16 → Hsv.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Avx2(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем маски в локальные переменные
            var shuffleHigh = HsvSse41Vectors.ShuffleGray16ToHighByte;
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var hsMask0 = HsvSse41Vectors.HsvGrayMask0;
            var hsMask1 = HsvSse41Vectors.HsvGrayMask1;

            while (count >= 16)
            {
                // Первые 8 пикселей
                var gray16_0 = Sse2.LoadVector128(src);
                var gray8_0 = Ssse3.Shuffle(gray16_0.AsByte(), shuffleHigh);

                var v0_0 = Sse2.And(Ssse3.Shuffle(gray8_0, shuffle0), hsMask0);
                var v1_0 = Sse2.And(Ssse3.Shuffle(gray8_0, shuffle1), hsMask1);

                Sse2.Store(dst, v0_0);
                Sse2.StoreLow((double*)(dst + 16), v1_0.AsDouble());

                // Вторые 8 пикселей
                var gray16_1 = Sse2.LoadVector128(src + 8);
                var gray8_1 = Ssse3.Shuffle(gray16_1.AsByte(), shuffleHigh);

                var v0_1 = Sse2.And(Ssse3.Shuffle(gray8_1, shuffle0), hsMask0);
                var v1_1 = Sse2.And(Ssse3.Shuffle(gray8_1, shuffle1), hsMask1);

                Sse2.Store(dst + 24, v0_1);
                Sse2.StoreLow((double*)(dst + 40), v1_1.AsDouble());

                src += 16;
                dst += 48;
                count -= 16;
            }

            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);
                var gray8 = Ssse3.Shuffle(gray16.AsByte(), shuffleHigh);

                var v0 = Sse2.And(Ssse3.Shuffle(gray8, shuffle0), hsMask0);
                var v1 = Sse2.And(Ssse3.Shuffle(gray8, shuffle1), hsMask1);

                Sse2.Store(dst, v0);
                Sse2.StoreLow((double*)(dst + 16), v1.AsDouble());

                src += 8;
                dst += 24;
                count -= 8;
            }

            while (count > 0)
            {
                var v = (byte)(*src >> 8);
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = v;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Hsv(Gray16 gray) => FromGray16(gray);
    public static explicit operator Gray16(Hsv hsv) => hsv.ToGray16();

    #endregion
}
