#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S1854, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Hsv ↔ Gray8.
/// </summary>
public readonly partial struct Hsv
{
    #region SIMD Constants

    // SIMD отключено — 4-байтовый формат HSV, SIMD требует повторного тестирования.
    private const HardwareAcceleration Gray8Implemented =
        HardwareAcceleration.None;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 в Hsv (H = 0, S = 0, V = value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Hsv FromGray8(Gray8 gray) => new(0, 0, gray.Value);

    /// <summary>Конвертирует Hsv в Gray8 (V8 напрямую).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => new(V);

    #endregion

    #region Batch Conversion (Hsv → Gray8)

    /// <summary>Пакетная конвертация Hsv → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Hsv> source, Span<Gray8> destination) =>
        ToGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void ToGray8(ReadOnlySpan<Hsv> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Hsv* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                ToGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray8Core(ReadOnlySpan<Hsv> source, Span<Gray8> destination, HardwareAcceleration selected)
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

    private static unsafe void ToGray8Parallel(Hsv* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Hsv>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray8Core(new ReadOnlySpan<Hsv>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Gray8 → Hsv)

    /// <summary>Пакетная конвертация Gray8 → Hsv.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Hsv> destination) =>
        FromGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Hsv с явным указанием ускорителя.</summary>
    public static unsafe void FromGray8(ReadOnlySpan<Gray8> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Hsv* dstPtr = destination)
                FromGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromGray8Core(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray8Core(ReadOnlySpan<Gray8> source, Span<Hsv> destination, HardwareAcceleration selected)
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

    private static unsafe void FromGray8Parallel(Gray8* source, Hsv* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Hsv>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray8Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<Hsv>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray8Scalar(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
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
    private static unsafe void FromGray8Scalar(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray8(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Hsv → Gray8)

    /// <summary>
    /// SSE41: Hsv → Gray8.
    /// Извлекает V (offset 3) из каждого 4-байтового HSV пикселя.
    /// 4 пикселя за итерацию (16 байт HSV → 4 байт Gray8).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Sse41(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // HSV = 4 байта: [H16][S8][V8], V на смещении 3
            var shuffleV = HsvSse41Vectors.ShuffleHsv4ToV;

            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src); // 4 пикселя HSV = 16 байт
                var v = Ssse3.Shuffle(hsv, shuffleV); // извлекаем V байты

                *(int*)dst = v.AsInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                *dst++ = *(src + 3); // V на смещении 3
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Hsv → Gray8)

    /// <summary>
    /// AVX2: Hsv → Gray8.
    /// 8 пикселей за итерацию (32 байт HSV → 8 байт Gray8).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Avx2(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // HSV = 4 байта: [H16][S8][V8], V на смещении 3
            var shuffleV = HsvSse41Vectors.ShuffleHsv4ToV;
            var shuffleV256 = Vector256.Create(shuffleV, shuffleV);

            while (count >= 8)
            {
                // Загружаем 8 пикселей HSV = 32 байта
                var hsv = Avx.LoadVector256(src);
                // VPSHUFB работает in-lane, извлекаем V из каждой 16-байтовой половины
                var v = Avx2.Shuffle(hsv.AsByte(), shuffleV256);

                // Нижняя lane: v[0..3], верхняя lane: v[16..19]
                // Нужно объединить: используем VPERMD или простое извлечение
                var lo = v.GetLower().AsInt32().GetElement(0);
                var hi = v.GetUpper().AsInt32().GetElement(0);
                *(int*)dst = lo;
                *(int*)(dst + 4) = hi;

                src += 32;
                dst += 8;
                count -= 8;
            }

            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src);
                var v = Ssse3.Shuffle(hsv, shuffleV);
                *(int*)dst = v.AsInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            while (count > 0)
            {
                *dst++ = *(src + 3);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Hsv)

    /// <summary>
    /// SSE41: Gray8 → Hsv.
    /// 4 пикселя за итерацию (4 байт Gray8 → 16 байт HSV).
    /// HSV = [H_lo, H_hi, S, V] = [0, 0, 0, gray]
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Sse41(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Shuffle: 4 Gray8 bytes → 4 HSV pixels (16 байт)
            var shuffle = HsvSse41Vectors.ShuffleGray8ToHsv4;

            while (count >= 4)
            {
                var gray = Sse2.LoadScalarVector128((int*)src).AsByte();
                var hsv = Ssse3.Shuffle(gray, shuffle);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0; // H_lo
                *dst++ = 0; // H_hi
                *dst++ = 0; // S
                *dst++ = v; // V
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Hsv)

    /// <summary>
    /// AVX2: Gray8 → Hsv.
    /// 8 пикселей за итерацию (8 байт Gray8 → 32 байт HSV).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Avx2(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Shuffle: 4 Gray8 bytes → 4 HSV pixels (16 байт) per lane
            var shuffle = HsvSse41Vectors.ShuffleGray8ToHsv4;

            while (count >= 8)
            {
                // Загружаем 8 Gray8 байт, нижние 4 в low lane, верхние 4 в high lane
                var grayLo = Sse2.LoadScalarVector128((int*)src).AsByte();
                var grayHi = Sse2.LoadScalarVector128((int*)(src + 4)).AsByte();

                var hsvLo = Ssse3.Shuffle(grayLo, shuffle);
                var hsvHi = Ssse3.Shuffle(grayHi, shuffle);

                hsvLo.Store(dst);
                hsvHi.Store(dst + 16);

                src += 8;
                dst += 32;
                count -= 8;
            }

            while (count >= 4)
            {
                var gray = Sse2.LoadScalarVector128((int*)src).AsByte();
                var hsv = Ssse3.Shuffle(gray, shuffle);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = v;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Hsv(Gray8 gray) => FromGray8(gray);
    public static explicit operator Gray8(Hsv hsv) => hsv.ToGray8();

    #endregion
}
