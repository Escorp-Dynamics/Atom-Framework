#pragma warning disable CA1000, CA2208, IDE0004, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ YCoCgR32.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для YCoCgR32.</summary>
    private const HardwareAcceleration YCoCgR32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует YCoCgR32 → Gray16 (Y масштабируется в 16-bit).
    /// </summary>
    /// <remarks>
    /// Value = Y * 257 = (Y &lt;&lt; 8) | Y.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromYCoCgR32(YCoCgR32 ycocg) => new((ushort)(ycocg.Y * 257));

    /// <summary>
    /// Конвертирует Gray16 → YCoCgR32.
    /// </summary>
    /// <remarks>
    /// Y = (Value * 255 + 32768) >> 16 ≈ Value / 257.
    /// Co=0, Cg=0 → упаковка: CoHigh=127, CgHigh=127, Frac=3.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32()
    {
        var y = (byte)(((Value * 255) + 32768) >> 16);
        return new YCoCgR32(y, 127, 127, 3);
    }

    #endregion

    #region Batch Conversion (Gray16 → YCoCgR32)

    /// <summary>Пакетная конвертация Gray16 → YCoCgR32.</summary>
    public static void ToYCoCgR32(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination) =>
        ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → YCoCgR32 с явным указанием ускорителя.</summary>
    public static unsafe void ToYCoCgR32(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCoCgR32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
                ToYCoCgR32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToYCoCgR32Core(source, destination, selected);
    }

    #endregion

    #region Batch Conversion (YCoCgR32 → Gray16)

    /// <summary>Пакетная конвертация YCoCgR32 → Gray16.</summary>
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination) =>
        FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCoCgR32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                FromYCoCgR32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromYCoCgR32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCoCgR32Core(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                ToYCoCgR32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                ToYCoCgR32Sse41(source, destination);
                break;
            default:
                ToYCoCgR32Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCoCgR32Core(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromYCoCgR32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromYCoCgR32Sse41(source, destination);
                break;
            default:
                FromYCoCgR32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void ToYCoCgR32Parallel(Gray16* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToYCoCgR32Core(new ReadOnlySpan<Gray16>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void FromYCoCgR32Parallel(YCoCgR32* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromYCoCgR32Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCoCgR32Scalar(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToYCoCgR32();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromYCoCgR32Scalar(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromYCoCgR32(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Gray16 → YCoCgR32.
    /// 8 пикселей за итерацию (16 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCoCgR32Sse41(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Кешируем константы
            var neutral127 = Gray16Sse41Vectors.Neutral127;
            var neutral3 = Gray16Sse41Vectors.Neutral3;
            var scale255 = Gray16Sse41Vectors.Scale255;

            while (i + 8 <= count)
            {
                // Загружаем 8 ushort
                var gray16 = Sse2.LoadVector128(src + i);

                // Y = (gray * 255) >> 16 ≈ MultiplyHigh
                var scaled = Sse2.MultiplyHigh(gray16, scale255);
                var yBytes = Sse2.PackUnsignedSaturate(scaled.AsInt16(), scaled.AsInt16());

                // Interleave с нейтральными значениями
                var yCo = Sse2.UnpackLow(yBytes, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);

                var result0 = Sse2.UnpackLow(yCo.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo.AsInt16(), cgFr.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());

                i += 8;
            }

            while (i < count)
            {
                destination[i] = source[i].ToYCoCgR32();
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Gray16.
    /// 8 пикселей за итерацию (32 байт → 16 байт).
    /// Gray16 = Y * 257 = (Y &lt;&lt; 8) | Y.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCoCgR32Sse41(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Shuffle для извлечения Y
            var shuffleY = Gray16Sse41Vectors.ShuffleYCoCgToY;

            while (i + 8 <= count)
            {
                var v0 = Sse2.LoadVector128(src + (i * 4));
                var v1 = Sse2.LoadVector128(src + (i * 4) + 16);

                var y0 = Ssse3.Shuffle(v0, shuffleY);
                var y1 = Ssse3.Shuffle(v1, shuffleY);

                // Объединяем: y0: Y0 Y1 Y2 Y3 0..., y1: Y4 Y5 Y6 Y7 0...
                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());

                // Расширяем Y в 16-bit: Y * 257 = Y | (Y << 8)
                var yBytes = y01.AsByte();
                var gray16 = Sse2.UnpackLow(yBytes, yBytes);

                Sse2.Store(dst + i, gray16.AsUInt16());

                i += 8;
            }

            while (i < count)
            {
                destination[i] = FromYCoCgR32(source[i]);
                i++;
            }
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Gray16 → YCoCgR32.
    /// 16 пикселей за итерацию (32 байт → 64 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCoCgR32Avx2(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var neutral127 = Gray16Sse41Vectors.Neutral127;
            var neutral3 = Gray16Sse41Vectors.Neutral3;
            var scale255 = Gray16Sse41Vectors.Scale255;

            while (i + 16 <= count)
            {
                // Загружаем 16 ushort как 2x Vector128
                var gray16Lo = Sse2.LoadVector128(src + i);
                var gray16Hi = Sse2.LoadVector128(src + i + 8);

                var scaledLo = Sse2.MultiplyHigh(gray16Lo, scale255);
                var scaledHi = Sse2.MultiplyHigh(gray16Hi, scale255);

                var yBytesLo = Sse2.PackUnsignedSaturate(scaledLo.AsInt16(), scaledLo.AsInt16());
                var yBytesHi = Sse2.PackUnsignedSaturate(scaledHi.AsInt16(), scaledHi.AsInt16());

                // First 8 pixels
                var yCo0 = Sse2.UnpackLow(yBytesLo, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);
                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo0.AsInt16(), cgFr.AsInt16());

                // Second 8 pixels
                var yCo1 = Sse2.UnpackLow(yBytesHi, neutral127);
                var result2 = Sse2.UnpackLow(yCo1.AsInt16(), cgFr.AsInt16());
                var result3 = Sse2.UnpackHigh(yCo1.AsInt16(), cgFr.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());
                Sse2.Store(dst + (i * 4) + 32, result2.AsByte());
                Sse2.Store(dst + (i * 4) + 48, result3.AsByte());

                i += 16;
            }

            if (i + 8 <= count)
            {
                ToYCoCgR32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = source[i].ToYCoCgR32();
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Gray16.
    /// 16 пикселей за итерацию (64 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCoCgR32Avx2(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var i = 0;
            var count = source.Length;

            var shuffleY = Gray16Sse41Vectors.ShuffleYCoCgToY;

            while (i + 16 <= count)
            {
                // Загружаем 16 пикселей (64 байт) как 4x Vector128
                var v0 = Sse2.LoadVector128(src + (i * 4));
                var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
                var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
                var v3 = Sse2.LoadVector128(src + (i * 4) + 48);

                var y0 = Ssse3.Shuffle(v0, shuffleY);
                var y1 = Ssse3.Shuffle(v1, shuffleY);
                var y2 = Ssse3.Shuffle(v2, shuffleY);
                var y3 = Ssse3.Shuffle(v3, shuffleY);

                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());
                var y23 = Sse2.UnpackLow(y2.AsUInt32(), y3.AsUInt32());
                var yAll = Sse2.UnpackLow(y01.AsUInt64(), y23.AsUInt64());

                // Расширяем в 16-bit
                var yBytes = yAll.AsByte();
                var gray16Lo = Sse2.UnpackLow(yBytes, yBytes);
                var gray16Hi = Sse2.UnpackHigh(yBytes, yBytes);

                Sse2.Store(dst + i, gray16Lo.AsUInt16());
                Sse2.Store(dst + i + 8, gray16Hi.AsUInt16());

                i += 16;
            }

            if (i + 8 <= count)
            {
                FromYCoCgR32Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromYCoCgR32(source[i]);
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Gray16 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Gray16 gray) => gray.ToYCoCgR32();

    /// <summary>Явная конвертация YCoCgR32 → Gray16.</summary>
    public static explicit operator Gray16(YCoCgR32 ycocg) => FromYCoCgR32(ycocg);

    #endregion
}
