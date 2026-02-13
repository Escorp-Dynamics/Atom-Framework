#pragma warning disable CA1000, CA2208, MA0051, S1481, S3236, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Rgba32.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    /// <summary>Реализованные ускорители для Rgba32.</summary>
    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Rgba32 → Gray16.</summary>
    /// <remarks>
    /// Сначала вычисляет Gray8, затем расширяет до 16-bit.
    /// Y = 0.299×R + 0.587×G + 0.114×B, затем v × 257.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromRgba32(Rgba32 rgba)
    {
        // Y = (19595×R + 38470×G + 7471×B + 32768) >> 16
        var y8 = (byte)(((19595 * rgba.R) + (38470 * rgba.G) + (7471 * rgba.B) + 32768) >> 16);
        // 8-bit → 16-bit: v × 257
        return new Gray16((ushort)(y8 | (y8 << 8)));
    }

    /// <summary>Конвертирует Gray16 → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32()
    {
        // 16-bit → 8-bit: v >> 8
        var v = (byte)(Value >> 8);
        return new Rgba32(v, v, v, 255);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Rgba32 → Gray16.</summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Gray16> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Rgba32 → Gray16 с явным ускорителем.</summary>
    public static unsafe void FromRgba32(
        ReadOnlySpan<Rgba32> source,
        Span<Gray16> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Rgba32* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
            {
                FromRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromRgba32Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Gray16 → Rgba32.</summary>
    public static void ToRgba32(ReadOnlySpan<Gray16> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Rgba32 с явным ускорителем.</summary>
    public static unsafe void ToRgba32(
        ReadOnlySpan<Gray16> source,
        Span<Rgba32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Rgba32* dstPtr = destination)
            {
                ToRgba32Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    private static void FromRgba32Core(
        ReadOnlySpan<Rgba32> source,
        Span<Gray16> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Rgba32* src = source)
                    fixed (Gray16* dst = destination)
                        FromRgba32Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 4:
                unsafe
                {
                    fixed (Rgba32* src = source)
                    fixed (Gray16* dst = destination)
                        FromRgba32Sse41(src, dst, source.Length);
                }
                break;

            default:
                FromRgba32Scalar(source, destination);
                break;
        }
    }

    private static void ToRgba32Core(
        ReadOnlySpan<Gray16> source,
        Span<Rgba32> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Rgba32* dst = destination)
                        ToRgba32Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 4:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Rgba32* dst = destination)
                        ToRgba32Sse41(src, dst, source.Length);
                }
                break;

            default:
                ToRgba32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromRgba32Parallel(
        Rgba32* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromRgba32Core(
                new ReadOnlySpan<Rgba32>(source + start, size),
                new Span<Gray16>(destination + start, size),
                selected);
        });
    }

    private static unsafe void ToRgba32Parallel(
        Gray16* source, Rgba32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToRgba32Core(
                new ReadOnlySpan<Gray16>(source + start, size),
                new Span<Rgba32>(destination + start, size),
                selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Scalar(ReadOnlySpan<Rgba32> source, Span<Gray16> destination)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromRgba32(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Scalar(ReadOnlySpan<Gray16> source, Span<Rgba32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToRgba32();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Rgba32 → Gray16.
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Sse41(Rgba32* src, Gray16* dst, int count)
    {
        var i = 0;

        var shuffleR = Gray16Sse41Vectors.ShuffleRgbaToR;
        var shuffleG = Gray16Sse41Vectors.ShuffleRgbaToG;
        var shuffleB = Gray16Sse41Vectors.ShuffleRgbaToB;
        var cR = Gray16Sse41Vectors.CoefficientR;
        var cG = Gray16Sse41Vectors.CoefficientG;
        var cB = Gray16Sse41Vectors.CoefficientB;
        var half = Gray16Sse41Vectors.Half;

        while (i + 4 <= count)
        {
            var rgba = Sse2.LoadVector128((byte*)(src + i));

            var rBytes = Ssse3.Shuffle(rgba, shuffleR);
            var gBytes = Ssse3.Shuffle(rgba, shuffleG);
            var bBytes = Ssse3.Shuffle(rgba, shuffleB);

            var r = Sse41.ConvertToVector128Int32(rBytes);
            var g = Sse41.ConvertToVector128Int32(gBytes);
            var b = Sse41.ConvertToVector128Int32(bBytes);

            // Y8 = (cR×R + cG×G + cB×B + half) >> 16
            var y = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR, r),
                    Sse41.MultiplyLow(cG, g)),
                    Sse41.MultiplyLow(cB, b)),
                    half), 16);

            // Y16 = Y8 | (Y8 << 8) = Y8 × 257
            var y16 = Sse41.MultiplyLow(y, Gray16Sse41Vectors.Scale8To16);

            // Pack int32 → int16 (4 значения → 4 ushort)
            var packed = Sse41.PackUnsignedSaturate(y16, y16);

            // Записываем 4 ushort
            *(ulong*)(dst + i) = packed.AsUInt64().GetElement(0);

            i += 4;
        }

        // Scalar остаток
        while (i < count)
        {
            dst[i] = FromRgba32(src[i]);
            i++;
        }
    }

    /// <summary>
    /// SSE41: Gray16 → Rgba32.
    /// 4 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Sse41(Gray16* src, Rgba32* dst, int count)
    {
        var i = 0;

        var shuffle = Gray8Sse41Vectors.ShuffleGrayToRgba;
        var alphaMask = Gray8Sse41Vectors.AlphaMask255;

        while (i + 4 <= count)
        {
            // Загружаем 4 ushort
            var gray16 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsUInt16();

            // v >> 8 → Gray8
            var gray8 = Sse2.ShiftRightLogical(gray16, 8);
            var packed = Sse2.PackUnsignedSaturate(gray8.AsInt16(), gray8.AsInt16());

            // Expand Gray8 → RGBA
            var rgba = Sse2.Or(Ssse3.Shuffle(packed.AsByte(), shuffle), alphaMask);

            // Записываем 4 RGBA пикселя
            Sse2.Store((byte*)(dst + i), rgba);

            i += 4;
        }

        // Scalar остаток
        while (i < count)
        {
            dst[i] = src[i].ToRgba32();
            i++;
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Rgba32 → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromRgba32Avx2(Rgba32* src, Gray16* dst, int count)
    {
        var i = 0;

        // Shuffle маски
        var shuffleR = Gray16Sse41Vectors.ShuffleRgbaToR;
        var shuffleG = Gray16Sse41Vectors.ShuffleRgbaToG;
        var shuffleB = Gray16Sse41Vectors.ShuffleRgbaToB;

        // Q16 коэффициенты (единые с SSE41!)
        var cR256 = Gray16Avx2Vectors.CoefficientR;
        var cG256 = Gray16Avx2Vectors.CoefficientG;
        var cB256 = Gray16Avx2Vectors.CoefficientB;
        var half256 = Gray16Avx2Vectors.Half;
        var scale257 = Gray16Avx2Vectors.Scale8To16;

        // === 8 пикселей за итерацию ===
        while (i + 8 <= count)
        {
            // Загружаем 8 RGBA пикселей (32 байт)
            var rgba0 = Sse2.LoadVector128((byte*)(src + i));
            var rgba1 = Sse2.LoadVector128((byte*)(src + i + 4));

            // Деинтерливинг R, G, B (4 байта в нижних позициях каждого Vector128)
            var rBytes0 = Ssse3.Shuffle(rgba0, shuffleR);
            var gBytes0 = Ssse3.Shuffle(rgba0, shuffleG);
            var bBytes0 = Ssse3.Shuffle(rgba0, shuffleB);

            var rBytes1 = Ssse3.Shuffle(rgba1, shuffleR);
            var gBytes1 = Ssse3.Shuffle(rgba1, shuffleG);
            var bBytes1 = Ssse3.Shuffle(rgba1, shuffleB);

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

        // SSE fallback для 4 пикселей
        var cR128 = Gray16Sse41Vectors.CoefficientR;
        var cG128 = Gray16Sse41Vectors.CoefficientG;
        var cB128 = Gray16Sse41Vectors.CoefficientB;
        var half128 = Gray16Sse41Vectors.Half;

        while (i + 4 <= count)
        {
            var rgba = Sse2.LoadVector128((byte*)(src + i));
            var rBytes = Ssse3.Shuffle(rgba, shuffleR);
            var gBytes = Ssse3.Shuffle(rgba, shuffleG);
            var bBytes = Ssse3.Shuffle(rgba, shuffleB);

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

        // Scalar остаток
        while (i < count)
        {
            dst[i] = FromRgba32(src[i]);
            i++;
        }
    }

    /// <summary>
    /// AVX2: Gray16 → Rgba32.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToRgba32Avx2(Gray16* src, Rgba32* dst, int count)
    {
        var i = 0;

        var shuffle = Gray8Sse41Vectors.ShuffleGrayToRgba;
        var alphaMask = Gray8Sse41Vectors.AlphaMask255;

        while (i + 8 <= count)
        {
            // Загружаем 8 ushort
            var gray16 = Sse2.LoadVector128((ushort*)(src + i));

            // v >> 8 → Gray8
            var shifted = Sse2.ShiftRightLogical(gray16, 8);
            var gray8 = Sse2.PackUnsignedSaturate(shifted.AsInt16(), shifted.AsInt16());

            // Expand Gray8 → RGBA (8 пикселей = 2 × 4)
            var g0 = gray8;
            var g1 = Sse2.ShiftRightLogical128BitLane(gray8, 4);

            var rgba0 = Sse2.Or(Ssse3.Shuffle(g0, shuffle), alphaMask);
            var rgba1 = Sse2.Or(Ssse3.Shuffle(g1, shuffle), alphaMask);

            // Записываем 8 RGBA пикселей
            Sse2.Store((byte*)(dst + i), rgba0);
            Sse2.Store((byte*)(dst + i + 4), rgba1);

            i += 8;
        }

        // SSE fallback
        while (i + 4 <= count)
        {
            var gray16 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsUInt16();
            var gray8 = Sse2.ShiftRightLogical(gray16, 8);
            var packed = Sse2.PackUnsignedSaturate(gray8.AsInt16(), gray8.AsInt16());
            var rgba = Sse2.Or(Ssse3.Shuffle(packed.AsByte(), shuffle), alphaMask);
            Sse2.Store((byte*)(dst + i), rgba);
            i += 4;
        }

        // Scalar остаток
        while (i < count)
        {
            dst[i] = src[i].ToRgba32();
            i++;
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Rgba32 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray16(Rgba32 rgba) => FromRgba32(rgba);

    /// <summary>Явное преобразование Gray16 → Rgba32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Rgba32(Gray16 gray) => gray.ToRgba32();

    #endregion
}
