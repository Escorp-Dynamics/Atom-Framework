#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Hsv (4-байтовый формат: H16, S8, V8).
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration HsvImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Hsv в Gray8 (V8 напрямую).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromHsv(Hsv hsv) => new(hsv.V);

    /// <summary>Конвертирует Gray8 в Hsv (H = 0, S = 0, V = value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv() => new(0, 0, Value);

    #endregion

    #region Batch Conversion (Gray8 → Hsv)

    /// <summary>Пакетная конвертация Gray8 → Hsv.</summary>
    public static void ToHsv(ReadOnlySpan<Gray8> source, Span<Hsv> destination) =>
        ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Hsv с явным указанием ускорителя.</summary>
    public static unsafe void ToHsv(ReadOnlySpan<Gray8> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Hsv* dstPtr = destination)
                ToHsvParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToHsvCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToHsvCore(ReadOnlySpan<Gray8> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToHsvAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToHsvSse41(source, destination);
                break;
            default:
                ToHsvScalar(source, destination);
                break;
        }
    }

    private static unsafe void ToHsvParallel(Gray8* source, Hsv* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToHsvCore(new ReadOnlySpan<Gray8>(source + start, size), new Span<Hsv>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Hsv → Gray8)

    /// <summary>Пакетная конвертация Hsv → Gray8.</summary>
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Gray8> destination) =>
        FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromHsv(ReadOnlySpan<Hsv> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Hsv* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromHsvParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromHsvCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromHsvCore(ReadOnlySpan<Hsv> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromHsvAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromHsvSse41(source, destination);
                break;
            default:
                FromHsvScalar(source, destination);
                break;
        }
    }

    private static unsafe void FromHsvParallel(Hsv* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromHsvCore(new ReadOnlySpan<Hsv>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToHsvScalar(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToHsv();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromHsvScalar(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromHsv(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Hsv)

    /// <summary>
    /// SSE41: Gray8 → Hsv (4-байтовый формат).
    /// Gray → (H_lo=0, H_hi=0, S=0, V=Gray).
    /// 4 пикселя за итерацию: 4 байт входа → 16 байт выхода.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToHsvSse41(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Hsv layout: [H_lo, H_hi, S, V] = 4 bytes per pixel
            // Gray8 → [0, 0, 0, Gray] for each pixel
            // Shuffle mask: G0 → (0,0,0,G0), G1 → (0,0,0,G1), G2 → (0,0,0,G2), G3 → (0,0,0,G3)
            // Input bytes:  [G0, G1, G2, G3, ...]
            // Output bytes: [0, 0, 0, G0, 0, 0, 0, G1, 0, 0, 0, G2, 0, 0, 0, G3]
            var shuffle4 = Gray8Sse41Vectors.ShuffleGrayToHsv;

            // 16 пикселей за итерацию (16 байт входа → 64 байт выхода)
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Первые 4 пикселя (байты 0-3)
                var hsv0 = Ssse3.Shuffle(gray, shuffle4);

                // Вторые 4 пикселя (байты 4-7) - сдвигаем вправо на 4 байта
                var gray1 = Sse2.ShiftRightLogical128BitLane(gray, 4);
                var hsv1 = Ssse3.Shuffle(gray1, shuffle4);

                // Третьи 4 пикселя (байты 8-11) - сдвигаем вправо на 8 байт
                var gray2 = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var hsv2 = Ssse3.Shuffle(gray2, shuffle4);

                // Четвёртые 4 пикселя (байты 12-15) - сдвигаем вправо на 12 байт
                var gray3 = Sse2.ShiftRightLogical128BitLane(gray, 12);
                var hsv3 = Ssse3.Shuffle(gray3, shuffle4);

                hsv0.Store(dst);
                hsv1.Store(dst + 16);
                hsv2.Store(dst + 32);
                hsv3.Store(dst + 48);

                src += 16;
                dst += 64;  // 16 пикселей × 4 байта = 64 байта
                count -= 16;
            }

            // 4 пикселя (4 байт входа → 16 байт выхода)
            while (count >= 4)
            {
                var gray = Sse2.LoadScalarVector128((int*)src).AsByte();
                var hsv = Ssse3.Shuffle(gray, shuffle4);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;   // H_lo
                *dst++ = 0;   // H_hi
                *dst++ = 0;   // S
                *dst++ = v;   // V
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Hsv)

    /// <summary>
    /// AVX2: Gray8 → Hsv (4-байтовый формат).
    /// 16 пикселей за итерацию с AVX2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToHsvAvx2(ReadOnlySpan<Gray8> source, Span<Hsv> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // AVX2: каждая 128-bit lane обрабатывается независимо
            var shuffle4 = Gray8Sse41Vectors.ShuffleGrayToHsv;

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Обрабатываем по 4 пикселя
                var hsv0 = Ssse3.Shuffle(gray, shuffle4);
                var gray1 = Sse2.ShiftRightLogical128BitLane(gray, 4);
                var hsv1 = Ssse3.Shuffle(gray1, shuffle4);
                var gray2 = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var hsv2 = Ssse3.Shuffle(gray2, shuffle4);
                var gray3 = Sse2.ShiftRightLogical128BitLane(gray, 12);
                var hsv3 = Ssse3.Shuffle(gray3, shuffle4);

                // Комбинируем в AVX2 векторы и записываем
                var out0 = Vector256.Create(hsv0, hsv1);
                var out1 = Vector256.Create(hsv2, hsv3);

                Avx.Store(dst, out0);
                Avx.Store(dst + 32, out1);

                src += 16;
                dst += 64;
                count -= 16;
            }

            // 4 пикселя (SSE fallback)
            while (count >= 4)
            {
                var gray = Sse2.LoadScalarVector128((int*)src).AsByte();
                var hsv = Ssse3.Shuffle(gray, shuffle4);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            // Scalar остаток
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

    #region SSE41 Implementation (Hsv → Gray8)

    /// <summary>
    /// SSE41: Hsv → Gray8 (4-байтовый формат).
    /// Извлекает V (4-й байт) из каждого 4-байтового Hsv.
    /// 4 пикселя за итерацию: 16 байт входа → 4 байт выхода.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromHsvSse41(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Hsv layout: [H_lo, H_hi, S, V] = 4 bytes per pixel
            // Извлекаем V из позиций 3, 7, 11, 15 (каждый 4-й байт)
            // Input:  [H_lo0, H_hi0, S0, V0, H_lo1, H_hi1, S1, V1, ...]
            // Output: [V0, V1, V2, V3, ...]
            var shuffleV = Gray8Sse41Vectors.ShuffleHsvToV;

            // 16 пикселей за итерацию (64 байт входа → 16 байт выхода)
            while (count >= 16)
            {
                var hsv0 = Sse2.LoadVector128(src);        // пиксели 0-3
                var hsv1 = Sse2.LoadVector128(src + 16);   // пиксели 4-7
                var hsv2 = Sse2.LoadVector128(src + 32);   // пиксели 8-11
                var hsv3 = Sse2.LoadVector128(src + 48);   // пиксели 12-15

                var v0 = Ssse3.Shuffle(hsv0, shuffleV);  // [V0,V1,V2,V3, 0,0,0,0, 0,0,0,0, 0,0,0,0]
                var v1 = Ssse3.Shuffle(hsv1, shuffleV);  // [V4,V5,V6,V7, 0,0,0,0, 0,0,0,0, 0,0,0,0]
                var v2 = Ssse3.Shuffle(hsv2, shuffleV);
                var v3 = Ssse3.Shuffle(hsv3, shuffleV);

                // Комбинируем: v0[0-3] | v1[0-3]<<32 | v2[0-3]<<64 | v3[0-3]<<96
                var lo = Sse2.UnpackLow(v0.AsUInt32(), v1.AsUInt32());   // [V0-3, V4-7, ?, ?]
                var hi = Sse2.UnpackLow(v2.AsUInt32(), v3.AsUInt32());   // [V8-11, V12-15, ?, ?]
                var result = Sse2.UnpackLow(lo.AsUInt64(), hi.AsUInt64()).AsByte();

                result.Store(dst);

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя (16 байт входа → 4 байт выхода)
            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src);
                var v = Ssse3.Shuffle(hsv, shuffleV);
                Unsafe.WriteUnaligned(dst, v.AsUInt32().GetElement(0));

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                *dst++ = *(src + 3);  // V - 4-й байт (индекс 3)
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Hsv → Gray8)

    /// <summary>
    /// AVX2: Hsv → Gray8 (4-байтовый формат).
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromHsvAvx2(ReadOnlySpan<Hsv> source, Span<Gray8> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleV = Vector128.Create(
                (byte)3, 7, 11, 15,
                0x80, 0x80, 0x80, 0x80,
                0x80, 0x80, 0x80, 0x80,
                0x80, 0x80, 0x80, 0x80);

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                var hsv0 = Sse2.LoadVector128(src);
                var hsv1 = Sse2.LoadVector128(src + 16);
                var hsv2 = Sse2.LoadVector128(src + 32);
                var hsv3 = Sse2.LoadVector128(src + 48);

                var v0 = Ssse3.Shuffle(hsv0, shuffleV);
                var v1 = Ssse3.Shuffle(hsv1, shuffleV);
                var v2 = Ssse3.Shuffle(hsv2, shuffleV);
                var v3 = Ssse3.Shuffle(hsv3, shuffleV);

                var lo = Sse2.UnpackLow(v0.AsUInt32(), v1.AsUInt32());
                var hi = Sse2.UnpackLow(v2.AsUInt32(), v3.AsUInt32());
                var result = Sse2.UnpackLow(lo.AsUInt64(), hi.AsUInt64()).AsByte();

                result.Store(dst);

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя (SSE fallback)
            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src);
                var v = Ssse3.Shuffle(hsv, shuffleV);
                Unsafe.WriteUnaligned(dst, v.AsUInt32().GetElement(0));

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                *dst++ = *(src + 3);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray8(Hsv hsv) => FromHsv(hsv);
    public static explicit operator Hsv(Gray8 gray) => gray.ToHsv();

    #endregion
}
