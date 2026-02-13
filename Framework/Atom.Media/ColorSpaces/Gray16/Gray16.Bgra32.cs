#pragma warning disable CA1000, CA2208, MA0051, S3236, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Bgra32.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    /// <summary>Реализованные ускорители для Bgra32.</summary>
    private const HardwareAcceleration Bgra32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Bgra32 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromBgra32(Bgra32 bgra)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y8 = (byte)(((19595 * bgra.R) + (38470 * bgra.G) + (7471 * bgra.B) + 32768) >> 16);
        return new Gray16((ushort)(y8 | (y8 << 8)));
    }

    /// <summary>Конвертирует Gray16 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgra32 ToBgra32()
    {
        var v = (byte)(Value >> 8);
        return new Bgra32(v, v, v, 255);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgra32 → Gray16.</summary>
    public static void FromBgra32(ReadOnlySpan<Bgra32> source, Span<Gray16> destination) =>
        FromBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgra32 → Gray16 с явным ускорителем.</summary>
    public static unsafe void FromBgra32(
        ReadOnlySpan<Bgra32> source,
        Span<Gray16> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgra32* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
            {
                FromBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromBgra32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Gray16 → Bgra32.</summary>
    public static void ToBgra32(ReadOnlySpan<Gray16> source, Span<Bgra32> destination) =>
        ToBgra32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Bgra32 с явным ускорителем.</summary>
    public static unsafe void ToBgra32(
        ReadOnlySpan<Gray16> source,
        Span<Bgra32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgra32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Bgra32* dstPtr = destination)
            {
                ToBgra32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToBgra32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    private static void FromBgra32Core(
        ReadOnlySpan<Bgra32> source,
        Span<Gray16> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Bgra32* src = source)
                    fixed (Gray16* dst = destination)
                        FromBgra32Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 4:
                unsafe
                {
                    fixed (Bgra32* src = source)
                    fixed (Gray16* dst = destination)
                        FromBgra32Sse41(src, dst, source.Length);
                }
                break;

            default:
                FromBgra32Scalar(source, destination);
                break;
        }
    }

    private static void ToBgra32Core(
        ReadOnlySpan<Gray16> source,
        Span<Bgra32> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Bgra32* dst = destination)
                        ToBgra32Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 4:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Bgra32* dst = destination)
                        ToBgra32Sse41(src, dst, source.Length);
                }
                break;

            default:
                ToBgra32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromBgra32Parallel(
        Bgra32* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgra32Core(
                new ReadOnlySpan<Bgra32>(source + start, size),
                new Span<Gray16>(destination + start, size),
                selected);
        });
    }

    private static unsafe void ToBgra32Parallel(
        Gray16* source, Bgra32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgra32Core(
                new ReadOnlySpan<Gray16>(source + start, size),
                new Span<Bgra32>(destination + start, size),
                selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgra32Scalar(ReadOnlySpan<Bgra32> source, Span<Gray16> destination)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgra32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgra32Scalar(ReadOnlySpan<Gray16> source, Span<Bgra32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToBgra32();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Bgra32 → Gray16.
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Sse41(Bgra32* src, Gray16* dst, int count)
    {
        var i = 0;

        var shuffleB = Gray16Sse41Vectors.ShuffleBgraToB;
        var shuffleG = Gray16Sse41Vectors.ShuffleBgraToG;
        var shuffleR = Gray16Sse41Vectors.ShuffleBgraToR;
        var cR = Gray16Sse41Vectors.CoefficientR;
        var cG = Gray16Sse41Vectors.CoefficientG;
        var cB = Gray16Sse41Vectors.CoefficientB;
        var half = Gray16Sse41Vectors.Half;

        while (i + 4 <= count)
        {
            var bgra = Sse2.LoadVector128((byte*)(src + i));

            var bBytes = Ssse3.Shuffle(bgra, shuffleB);
            var gBytes = Ssse3.Shuffle(bgra, shuffleG);
            var rBytes = Ssse3.Shuffle(bgra, shuffleR);

            var r = Sse41.ConvertToVector128Int32(rBytes);
            var g = Sse41.ConvertToVector128Int32(gBytes);
            var b = Sse41.ConvertToVector128Int32(bBytes);

            var y = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR, r),
                    Sse41.MultiplyLow(cG, g)),
                    Sse41.MultiplyLow(cB, b)),
                    half), 16);

            var y16 = Sse41.MultiplyLow(y, Gray16Sse41Vectors.Scale8To16);
            var packed = Sse41.PackUnsignedSaturate(y16, y16);

            *(ulong*)(dst + i) = packed.AsUInt64().GetElement(0);

            i += 4;
        }

        while (i < count)
        {
            dst[i] = FromBgra32(src[i]);
            i++;
        }
    }

    /// <summary>
    /// SSE41: Gray16 → Bgra32.
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Sse41(Gray16* src, Bgra32* dst, int count)
    {
        var i = 0;

        var shuffle = Gray8Sse41Vectors.ShuffleGrayToRgba;
        var alphaMask = Gray8Sse41Vectors.AlphaMask255;

        while (i + 4 <= count)
        {
            var gray16 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsUInt16();
            var gray8 = Sse2.ShiftRightLogical(gray16, 8);
            var packed = Sse2.PackUnsignedSaturate(gray8.AsInt16(), gray8.AsInt16());
            var bgra = Sse2.Or(Ssse3.Shuffle(packed.AsByte(), shuffle), alphaMask);

            Sse2.Store((byte*)(dst + i), bgra);

            i += 4;
        }

        while (i < count)
        {
            dst[i] = src[i].ToBgra32();
            i++;
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Bgra32 → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgra32Avx2(Bgra32* src, Gray16* dst, int count)
    {
        var i = 0;

        // Shuffle маски
        var shuffleB = Gray16Sse41Vectors.ShuffleBgraToB;
        var shuffleG = Gray16Sse41Vectors.ShuffleBgraToG;
        var shuffleR = Gray16Sse41Vectors.ShuffleBgraToR;

        // Q16 коэффициенты (единые с SSE41!)
        var cR256 = Gray16Avx2Vectors.CoefficientR;
        var cG256 = Gray16Avx2Vectors.CoefficientG;
        var cB256 = Gray16Avx2Vectors.CoefficientB;
        var half256 = Gray16Avx2Vectors.Half;
        var scale257 = Gray16Avx2Vectors.Scale8To16;

        // === 8 пикселей за итерацию ===
        while (i + 8 <= count)
        {
            var bgra0 = Sse2.LoadVector128((byte*)(src + i));
            var bgra1 = Sse2.LoadVector128((byte*)(src + i + 4));

            // Деинтерливинг B, G, R (4 байта в нижних позициях каждого Vector128)
            var bBytes0 = Ssse3.Shuffle(bgra0, shuffleB);
            var gBytes0 = Ssse3.Shuffle(bgra0, shuffleG);
            var rBytes0 = Ssse3.Shuffle(bgra0, shuffleR);

            var bBytes1 = Ssse3.Shuffle(bgra1, shuffleB);
            var gBytes1 = Ssse3.Shuffle(bgra1, shuffleG);
            var rBytes1 = Ssse3.Shuffle(bgra1, shuffleR);

            // Расширяем byte → int32 (4 пикселя → Vector128<int>)
            var r0 = Sse41.ConvertToVector128Int32(rBytes0);
            var g0 = Sse41.ConvertToVector128Int32(gBytes0);
            var b0 = Sse41.ConvertToVector128Int32(bBytes0);

            var r1 = Sse41.ConvertToVector128Int32(rBytes1);
            var g1 = Sse41.ConvertToVector128Int32(gBytes1);
            var b1 = Sse41.ConvertToVector128Int32(bBytes1);

            // Объединяем в AVX2 регистры (8 int32)
            var r256 = Vector256.Create(r0, r1);
            var g256 = Vector256.Create(g0, g1);
            var b256 = Vector256.Create(b0, b1);

            // Y8 = (cR×R + cG×G + cB×B + half) >> 16 (Q16)
            var y8 = Avx2.ShiftRightArithmetic(
                Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cR256, r256),
                    Avx2.MultiplyLow(cG256, g256)),
                    Avx2.MultiplyLow(cB256, b256)),
                    half256), 16);

            // Y16 = Y8 × 257
            var y16 = Avx2.MultiplyLow(y8, scale257);

            // Pack int32 → uint16 (8 × int32 → 8 × uint16, с насыщением)
            var packed = Avx2.PackUnsignedSaturate(y16, y16);
            // Результат: [lo0,lo1,hi0,hi1] → нужно переупорядочить
            var reordered = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsUInt16();

            // Записываем 8 ushort (16 байт)
            Sse2.Store((ushort*)(dst + i), reordered.GetLower());

            i += 8;
        }

        // SSE fallback
        var cR128 = Gray16Sse41Vectors.CoefficientR;
        var cG128 = Gray16Sse41Vectors.CoefficientG;
        var cB128 = Gray16Sse41Vectors.CoefficientB;
        var half128 = Gray16Sse41Vectors.Half;

        while (i + 4 <= count)
        {
            var bgra = Sse2.LoadVector128((byte*)(src + i));
            var bBytes = Ssse3.Shuffle(bgra, shuffleB);
            var gBytes = Ssse3.Shuffle(bgra, shuffleG);
            var rBytes = Ssse3.Shuffle(bgra, shuffleR);

            var r = Sse41.ConvertToVector128Int32(rBytes);
            var g = Sse41.ConvertToVector128Int32(gBytes);
            var b = Sse41.ConvertToVector128Int32(bBytes);

            var y = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR128, r),
                    Sse41.MultiplyLow(cG128, g)),
                    Sse41.MultiplyLow(cB128, b)),
                    half128), 16);

            var y16 = Sse41.MultiplyLow(y, Gray16Sse41Vectors.Scale8To16);
            var packed = Sse41.PackUnsignedSaturate(y16, y16);
            *(ulong*)(dst + i) = packed.AsUInt64().GetElement(0);

            i += 4;
        }

        while (i < count)
        {
            dst[i] = FromBgra32(src[i]);
            i++;
        }
    }

    /// <summary>
    /// AVX2: Gray16 → Bgra32.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgra32Avx2(Gray16* src, Bgra32* dst, int count)
    {
        var i = 0;

        var shuffle = Gray8Sse41Vectors.ShuffleGrayToRgba;
        var alphaMask = Gray8Sse41Vectors.AlphaMask255;

        while (i + 8 <= count)
        {
            var gray16 = Sse2.LoadVector128((ushort*)(src + i));
            var shifted = Sse2.ShiftRightLogical(gray16, 8);
            var gray8 = Sse2.PackUnsignedSaturate(shifted.AsInt16(), shifted.AsInt16());

            var g0 = gray8;
            var g1 = Sse2.ShiftRightLogical128BitLane(gray8, 4);

            var bgra0 = Sse2.Or(Ssse3.Shuffle(g0, shuffle), alphaMask);
            var bgra1 = Sse2.Or(Ssse3.Shuffle(g1, shuffle), alphaMask);

            Sse2.Store((byte*)(dst + i), bgra0);
            Sse2.Store((byte*)(dst + i + 4), bgra1);

            i += 8;
        }

        while (i + 4 <= count)
        {
            var gray16 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsUInt16();
            var gray8 = Sse2.ShiftRightLogical(gray16, 8);
            var packed = Sse2.PackUnsignedSaturate(gray8.AsInt16(), gray8.AsInt16());
            var bgra = Sse2.Or(Ssse3.Shuffle(packed.AsByte(), shuffle), alphaMask);
            Sse2.Store((byte*)(dst + i), bgra);
            i += 4;
        }

        while (i < count)
        {
            dst[i] = src[i].ToBgra32();
            i++;
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgra32 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray16(Bgra32 bgra) => FromBgra32(bgra);

    /// <summary>Явное преобразование Gray16 → Bgra32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgra32(Gray16 gray) => gray.ToBgra32();

    #endregion
}
