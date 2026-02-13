#pragma warning disable CA1000, CA2208, IDE0022, MA0051, S3236, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Gray8.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    /// <summary>Реализованные ускорители для Gray8.</summary>
    private const HardwareAcceleration Gray8Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Gray8 → Gray16 (расширение до 16-bit).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromGray8(Gray8 gray)
    {
        // 8-bit → 16-bit: v × 257 = v | (v << 8) для полного диапазона
        // 0 → 0, 255 → 65535
        var v = gray.Value;
        return new Gray16((ushort)(v | (v << 8)));
    }

    /// <summary>Конвертирует Gray16 → Gray8 (сжатие до 8-bit).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8()
    {
        // 16-bit → 8-bit: v >> 8 (старший байт)
        return new Gray8((byte)(Value >> 8));
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Gray8 → Gray16.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<Gray16> destination) =>
        FromGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Gray16 с явным ускорителем.</summary>
    public static unsafe void FromGray8(
        ReadOnlySpan<Gray8> source,
        Span<Gray16> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
            {
                FromGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromGray8Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Gray16 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<Gray16> source, Span<Gray8> destination) =>
        ToGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Gray8 с явным ускорителем.</summary>
    public static unsafe void ToGray8(
        ReadOnlySpan<Gray16> source,
        Span<Gray8> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
            {
                ToGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToGray8Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    private static void FromGray8Core(
        ReadOnlySpan<Gray8> source,
        Span<Gray16> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Gray8* src = source)
                    fixed (Gray16* dst = destination)
                        FromGray8Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 8:
                unsafe
                {
                    fixed (Gray8* src = source)
                    fixed (Gray16* dst = destination)
                        FromGray8Sse41(src, dst, source.Length);
                }
                break;

            default:
                FromGray8Scalar(source, destination);
                break;
        }
    }

    private static void ToGray8Core(
        ReadOnlySpan<Gray16> source,
        Span<Gray8> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Gray8* dst = destination)
                        ToGray8Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 8:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Gray8* dst = destination)
                        ToGray8Sse41(src, dst, source.Length);
                }
                break;

            default:
                ToGray8Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromGray8Parallel(
        Gray8* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromGray8Core(
                new ReadOnlySpan<Gray8>(source + start, size),
                new Span<Gray16>(destination + start, size),
                selected);
        });
    }

    private static unsafe void ToGray8Parallel(
        Gray16* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToGray8Core(
                new ReadOnlySpan<Gray16>(source + start, size),
                new Span<Gray8>(destination + start, size),
                selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromGray8Scalar(ReadOnlySpan<Gray8> source, Span<Gray16> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray8(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray8Scalar(ReadOnlySpan<Gray16> source, Span<Gray8> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToGray8();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Gray8 → Gray16.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Sse41(Gray8* src, Gray16* dst, int count)
    {
        var i = 0;

        // 8 пикселей за итерацию
        while (i + 8 <= count)
        {
            // Загружаем 8 байт Gray8
            var gray8 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsByte();

            // Zero-extend byte → ushort: v → (0, v)
            var gray16Lo = Sse41.ConvertToVector128Int16(gray8).AsUInt16();

            // v × 257 = v | (v << 8) для полного диапазона 0-65535
            var shifted = Sse2.ShiftLeftLogical(gray16Lo, 8);
            var result = Sse2.Or(gray16Lo, shifted);

            // Записываем 8 ushort
            Sse2.Store((ushort*)(dst + i), result);

            i += 8;
        }

        // Scalar остаток
        while (i < count)
        {
            var v = src[i].Value;
            dst[i] = new Gray16((ushort)(v | (v << 8)));
            i++;
        }
    }

    /// <summary>
    /// SSE41: Gray16 → Gray8.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Sse41(Gray16* src, Gray8* dst, int count)
    {
        var i = 0;

        // 8 пикселей за итерацию
        while (i + 8 <= count)
        {
            // Загружаем 8 ushort
            var gray16 = Sse2.LoadVector128((ushort*)(src + i));

            // v >> 8 (старший байт)
            var shifted = Sse2.ShiftRightLogical(gray16, 8);

            // Pack ushort → byte (только младшие 8 бит)
            var packed = Sse2.PackUnsignedSaturate(shifted.AsInt16(), shifted.AsInt16());

            // Записываем 8 байт
            *(ulong*)(dst + i) = packed.AsUInt64().GetElement(0);

            i += 8;
        }

        // Scalar остаток
        while (i < count)
        {
            dst[i] = new Gray8((byte)(src[i].Value >> 8));
            i++;
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Gray8 → Gray16.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Avx2(Gray8* src, Gray16* dst, int count)
    {
        var i = 0;

        // 16 пикселей за итерацию
        while (i + 16 <= count)
        {
            // Загружаем 16 байт Gray8
            var gray8 = Sse2.LoadVector128((byte*)(src + i));

            // Zero-extend byte → ushort (16 пикселей)
            var gray16 = Avx2.ConvertToVector256Int16(gray8).AsUInt16();

            // v × 257 = v | (v << 8)
            var shifted = Avx2.ShiftLeftLogical(gray16, 8);
            var result = Avx2.Or(gray16, shifted);

            // Записываем 16 ushort
            Avx.Store((ushort*)(dst + i), result);

            i += 16;
        }

        // SSE fallback
        while (i + 8 <= count)
        {
            var gray8 = Sse2.LoadScalarVector128((ulong*)(src + i)).AsByte();
            var gray16Lo = Sse41.ConvertToVector128Int16(gray8).AsUInt16();
            var shifted = Sse2.ShiftLeftLogical(gray16Lo, 8);
            var result = Sse2.Or(gray16Lo, shifted);
            Sse2.Store((ushort*)(dst + i), result);
            i += 8;
        }

        // Scalar остаток
        while (i < count)
        {
            var v = src[i].Value;
            dst[i] = new Gray16((ushort)(v | (v << 8)));
            i++;
        }
    }

    /// <summary>
    /// AVX2: Gray16 → Gray8.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Avx2(Gray16* src, Gray8* dst, int count)
    {
        var i = 0;

        // 16 пикселей за итерацию
        while (i + 16 <= count)
        {
            // Загружаем 16 ushort
            var gray16 = Avx.LoadVector256((ushort*)(src + i));

            // v >> 8 (старший байт)
            var shifted = Avx2.ShiftRightLogical(gray16, 8);

            // Pack ushort → byte
            var packed = Avx2.PackUnsignedSaturate(shifted.AsInt16(), shifted.AsInt16());

            // Permute для правильного порядка (AVX2 pack работает in-lane)
            var result = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte();

            // Записываем 16 байт
            Sse2.Store((byte*)(dst + i), result.GetLower());

            i += 16;
        }

        // SSE fallback
        while (i + 8 <= count)
        {
            var gray16 = Sse2.LoadVector128((ushort*)(src + i));
            var shifted = Sse2.ShiftRightLogical(gray16, 8);
            var packed = Sse2.PackUnsignedSaturate(shifted.AsInt16(), shifted.AsInt16());
            *(ulong*)(dst + i) = packed.AsUInt64().GetElement(0);
            i += 8;
        }

        // Scalar остаток
        while (i < count)
        {
            dst[i] = new Gray8((byte)(src[i].Value >> 8));
            i++;
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Gray8 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray16(Gray8 gray) => FromGray8(gray);

    /// <summary>Явное преобразование Gray16 → Gray8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray8(Gray16 gray) => gray.ToGray8();

    #endregion
}
