#pragma warning disable CA1000, CA2208, IDE0004, IDE0017, MA0051, S864, S3776, S4136, S4144, SA1407, RCS1032

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Rgb24 ↔ Rgba32.
/// Простая shuffle-операция: параллелизация не нужна (overhead > выигрыш).
/// </summary>
public readonly partial struct Rgba32
{
    #region SIMD Constants

    /// <summary>
    /// Реализованные ускорители для конвертации Rgb24 ↔ Rgba32.
    /// </summary>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Rgb24 в Rgba32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromRgb24(Rgb24 rgb) => new(rgb.R, rgb.G, rgb.B, 255);

    /// <summary>Конвертирует Rgba32 в Rgb24 (отбрасывает альфа-канал).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24() => new(R, G, B);

    #endregion

    #region Batch Conversion (Rgb24 → Rgba32)

    /// <summary>
    /// Пакетная конвертация Rgb24 → Rgba32 с SIMD.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Rgba32 с явным указанием ускорителя.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 16:
                FromRgb24Avx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromRgb24Sse41(source, destination);
                break;
            default:
                FromRgb24Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Batch Conversion (Rgba32 → Rgb24)

    /// <summary>
    /// Пакетная конвертация Rgba32 → Rgb24 с SIMD.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Rgb24 с явным указанием ускорителя.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        switch (selected)
        {
            case HardwareAcceleration.Avx512BW when source.Length >= 18:
                ToRgb24Avx512(source, destination);
                break;
            case HardwareAcceleration.Avx2 when source.Length >= 18:
                ToRgb24Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 6:
                ToRgb24Sse41(source, destination);
                break;
            default:
                ToRgb24Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Scalar Implementation

    /// <summary>
    /// Оптимизированная скалярная версия Rgb24 → Rgba32.
    /// Использует OR-only без AND: читаем 4 байта, OR с 0xFF000000 перезаписывает 4-й байт.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Scalar(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var s = (byte*)srcPtr;
            var d = (uint*)dstPtr;
            var count = source.Length;

            // 8x unroll
            while (count >= 8)
            {
                d[0] = (*(uint*)(s + 0)) | 0xFF000000;
                d[1] = (*(uint*)(s + 3)) | 0xFF000000;
                d[2] = (*(uint*)(s + 6)) | 0xFF000000;
                d[3] = (*(uint*)(s + 9)) | 0xFF000000;
                d[4] = (*(uint*)(s + 12)) | 0xFF000000;
                d[5] = (*(uint*)(s + 15)) | 0xFF000000;
                d[6] = (*(uint*)(s + 18)) | 0xFF000000;
                d[7] = (*(uint*)(s + 21)) | 0xFF000000;

                s += 24;
                d += 8;
                count -= 8;
            }

            // 4x fallback
            while (count >= 4)
            {
                d[0] = (*(uint*)(s + 0)) | 0xFF000000;
                d[1] = (*(uint*)(s + 3)) | 0xFF000000;
                d[2] = (*(uint*)(s + 6)) | 0xFF000000;
                d[3] = (*(uint*)(s + 9)) | 0xFF000000;

                s += 12;
                d += 4;
                count -= 4;
            }

            // Остаток побайтово
            var dst = (byte*)d;
            while (count > 0)
            {
                dst[0] = s[0];
                dst[1] = s[1];
                dst[2] = s[2];
                dst[3] = 255;

                s += 3;
                dst += 4;
                count--;
            }
        }
    }

    /// <summary>
    /// Оптимизированная скалярная версия Rgba32 → Rgb24.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Scalar(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var s = (uint*)srcPtr;
            var d = (uint*)dstPtr;
            var count = source.Length;

            // 8x unroll
            while (count >= 8)
            {
                var p0 = s[0];
                var p1 = s[1];
                var p2 = s[2];
                var p3 = s[3];
                var p4 = s[4];
                var p5 = s[5];
                var p6 = s[6];
                var p7 = s[7];

                d[0] = (p0 & 0x00FFFFFF) | (p1 << 24);
                d[1] = ((p1 >> 8) & 0xFFFF) | (p2 << 16);
                d[2] = ((p2 >> 16) & 0xFF) | ((p3 & 0x00FFFFFF) << 8);

                d[3] = (p4 & 0x00FFFFFF) | (p5 << 24);
                d[4] = ((p5 >> 8) & 0xFFFF) | (p6 << 16);
                d[5] = ((p6 >> 16) & 0xFF) | ((p7 & 0x00FFFFFF) << 8);

                s += 8;
                d += 6;
                count -= 8;
            }

            // 4x fallback
            while (count >= 4)
            {
                var p0 = s[0];
                var p1 = s[1];
                var p2 = s[2];
                var p3 = s[3];

                d[0] = (p0 & 0x00FFFFFF) | (p1 << 24);
                d[1] = ((p1 >> 8) & 0xFFFF) | (p2 << 16);
                d[2] = ((p2 >> 16) & 0xFF) | ((p3 & 0x00FFFFFF) << 8);

                s += 4;
                d += 3;
                count -= 4;
            }

            // Остаток побайтово
            var src = (byte*)s;
            var dst = (byte*)d;
            while (count > 0)
            {
                dst[0] = src[0];
                dst[1] = src[1];
                dst[2] = src[2];

                src += 4;
                dst += 3;
                count--;
            }
        }
    }

    #endregion

    #region SSE4.1 Implementation

    /// <summary>
    /// SSE4.1 версия Rgb24 → Rgba32 — 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Sse41(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask = Rgba32Sse41Vectors.Rgb24ToRgba32ShuffleMask;
            var alphaMask = Rgba32Sse41Vectors.Alpha255Mask;

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadVector128(src + 12);
                var v2 = Sse2.LoadVector128(src + 24);
                var v3 = Sse2.LoadVector128(src + 36);

                Sse2.Store(dst, Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask));
                Sse2.Store(dst + 16, Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask));
                Sse2.Store(dst + 32, Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask));
                Sse2.Store(dst + 48, Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask));

                src += 48;
                dst += 64;
                count -= 16;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var v = Sse2.LoadVector128(src);
                Sse2.Store(dst, Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask));

                src += 12;
                dst += 16;
                count -= 4;
            }

            // Остаток
            if (count > 0)
                FromRgb24Scalar(new ReadOnlySpan<Rgb24>(src, count), new Span<Rgba32>(dst, count));
        }
    }

    /// <summary>
    /// SSE4.1 версия Rgba32 → Rgb24 — 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Sse41(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask = Rgba32Sse41Vectors.Rgba32ToRgb24ShuffleMask;

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadVector128(src + 16);
                var v2 = Sse2.LoadVector128(src + 32);
                var v3 = Sse2.LoadVector128(src + 48);

                Sse2.Store(dst, Ssse3.Shuffle(v0, shuffleMask));
                Sse2.Store(dst + 12, Ssse3.Shuffle(v1, shuffleMask));
                Sse2.Store(dst + 24, Ssse3.Shuffle(v2, shuffleMask));
                Sse2.Store(dst + 36, Ssse3.Shuffle(v3, shuffleMask));

                src += 64;
                dst += 48;
                count -= 16;
            }

            // 4 пикселя с overlapping stores
            while (count >= 6)
            {
                var v = Sse2.LoadVector128(src);
                var r = Ssse3.Shuffle(v, shuffleMask);
                Sse2.StoreLow((double*)dst, r.AsDouble());
                Sse2.StoreLow((double*)(dst + 4), Sse2.ShiftRightLogical128BitLane(r, 4).AsDouble());

                src += 16;
                dst += 12;
                count -= 4;
            }

            // Остаток
            if (count > 0)
                ToRgb24Scalar(new ReadOnlySpan<Rgba32>(src, count), new Span<Rgb24>(dst, count));
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2 версия Rgb24 → Rgba32 — 16 пикселей за итерацию.
    /// Использует SSE shuffle (24-bit overlapping не даёт AVX2 преимуществ).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx2(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask = Rgba32Sse41Vectors.Rgb24ToRgba32ShuffleMask;
            var alphaMask = Rgba32Sse41Vectors.Alpha255Mask;

            // 16 пикселей за итерацию
            while (count >= 16)
            {
                var v0 = Sse2.LoadVector128(src);
                var v1 = Sse2.LoadVector128(src + 12);
                var v2 = Sse2.LoadVector128(src + 24);
                var v3 = Sse2.LoadVector128(src + 36);

                Sse2.Store(dst, Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask));
                Sse2.Store(dst + 16, Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask));
                Sse2.Store(dst + 32, Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask));
                Sse2.Store(dst + 48, Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask));

                src += 48;
                dst += 64;
                count -= 16;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var v = Sse2.LoadVector128(src);
                Sse2.Store(dst, Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask));

                src += 12;
                dst += 16;
                count -= 4;
            }

            // Остаток
            if (count > 0)
                FromRgb24Scalar(new ReadOnlySpan<Rgb24>(src, count), new Span<Rgba32>(dst, count));
        }
    }

    /// <summary>
    /// AVX2 версия Rgba32 → Rgb24 — 16 пикселей за итерацию.
    /// Использует 256-bit loads + shuffle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx2(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask256 = Rgba32Avx2Vectors.Rgba32ToRgb24ShuffleMask;

            // 16 пикселей за итерацию (overlapping store требует +2 запаса)
            while (count >= 18)
            {
                var v0 = Avx.LoadVector256(src);
                var v1 = Avx.LoadVector256(src + 32);

                var s0 = Avx2.Shuffle(v0, shuffleMask256);
                var s1 = Avx2.Shuffle(v1, shuffleMask256);

                Sse2.Store(dst, s0.GetLower());
                Sse2.Store(dst + 12, s0.GetUpper());
                Sse2.Store(dst + 24, s1.GetLower());
                Sse2.Store(dst + 36, s1.GetUpper());

                src += 64;
                dst += 48;
                count -= 16;
            }

            // 8 пикселей
            while (count >= 10)
            {
                var v = Avx.LoadVector256(src);
                var s = Avx2.Shuffle(v, shuffleMask256);

                Sse2.Store(dst, s.GetLower());
                Sse2.Store(dst + 12, s.GetUpper());

                src += 32;
                dst += 24;
                count -= 8;
            }

            // Остаток
            if (count > 0)
                ToRgb24Scalar(new ReadOnlySpan<Rgba32>(src, count), new Span<Rgb24>(dst, count));
        }
    }

    #endregion

    #region AVX512BW Implementation

    /// <summary>
    /// AVX512BW версия Rgb24 → Rgba32 — 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgb24Avx512(ReadOnlySpan<Rgb24> source, Span<Rgba32> destination)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask = Rgba32Sse41Vectors.Rgb24ToRgba32ShuffleMask;
            var alphaMask = Rgba32Sse41Vectors.Alpha255Mask;

            // 16 пикселей за итерацию с 512-bit store
            while (count >= 16)
            {
                var rgb0 = Sse2.LoadVector128(src);
                var rgb1 = Sse2.LoadVector128(src + 12);
                var rgb2 = Sse2.LoadVector128(src + 24);
                var rgb3 = Sse2.LoadVector128(src + 36);

                var rgba0 = Sse2.Or(Ssse3.Shuffle(rgb0, shuffleMask), alphaMask);
                var rgba1 = Sse2.Or(Ssse3.Shuffle(rgb1, shuffleMask), alphaMask);
                var rgba2 = Sse2.Or(Ssse3.Shuffle(rgb2, shuffleMask), alphaMask);
                var rgba3 = Sse2.Or(Ssse3.Shuffle(rgb3, shuffleMask), alphaMask);

                var lo256 = Vector256.Create(rgba0, rgba1);
                var hi256 = Vector256.Create(rgba2, rgba3);
                Avx512BW.Store(dst, Vector512.Create(lo256, hi256));

                src += 48;
                dst += 64;
                count -= 16;
            }

            // Остаток через SSE
            if (count >= 4)
                FromRgb24Sse41(new ReadOnlySpan<Rgb24>(src, count), new Span<Rgba32>(dst, count));
            else if (count > 0)
                FromRgb24Scalar(new ReadOnlySpan<Rgb24>(src, count), new Span<Rgba32>(dst, count));
        }
    }

    /// <summary>
    /// AVX512BW версия Rgba32 → Rgb24 — 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgb24Avx512(ReadOnlySpan<Rgba32> source, Span<Rgb24> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleMask = Rgba32Sse41Vectors.Rgba32ToRgb24ShuffleMask;

            // 16 пикселей за итерацию с 512-bit load
            while (count >= 18)
            {
                var rgba = Avx512BW.LoadVector512(src);

                var v0 = rgba.GetLower().GetLower();
                var v1 = rgba.GetLower().GetUpper();
                var v2 = rgba.GetUpper().GetLower();
                var v3 = rgba.GetUpper().GetUpper();

                Sse2.Store(dst, Ssse3.Shuffle(v0, shuffleMask));
                Sse2.Store(dst + 12, Ssse3.Shuffle(v1, shuffleMask));
                Sse2.Store(dst + 24, Ssse3.Shuffle(v2, shuffleMask));
                Sse2.Store(dst + 36, Ssse3.Shuffle(v3, shuffleMask));

                src += 64;
                dst += 48;
                count -= 16;
            }

            // Остаток через SSE
            if (count >= 6)
                ToRgb24Sse41(new ReadOnlySpan<Rgba32>(src, count), new Span<Rgb24>(dst, count));
            else if (count > 0)
                ToRgb24Scalar(new ReadOnlySpan<Rgba32>(src, count), new Span<Rgb24>(dst, count));
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация из Rgb24 в Rgba32.</summary>
    public static explicit operator Rgba32(Rgb24 rgb) => FromRgb24(rgb);

    /// <summary>Явная конвертация из Rgba32 в Rgb24.</summary>
    public static explicit operator Rgb24(Rgba32 rgba) => rgba.ToRgb24();

    #endregion
}
