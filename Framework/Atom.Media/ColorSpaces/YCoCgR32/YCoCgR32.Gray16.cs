#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Gray16.
/// Gray16 использует только компонент Y (luma) из YCoCgR32 с масштабированием.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Gray16.</summary>
    private const HardwareAcceleration Gray16Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Gray16 → YCoCgR32 (Y=gray>>8, Co=0, Cg=0).
    /// </summary>
    /// <remarks>
    /// Y = (Value * 255 + 32768) >> 16 ≈ Value / 257.
    /// Co=0, Cg=0 → упаковка: CoHigh=127, CgHigh=127, Frac=3.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromGray16(Gray16 gray)
    {
        var y = (byte)(((gray.Value * 255) + 32768) >> 16);
        return new YCoCgR32(y, 127, 127, 3);
    }

    /// <summary>
    /// Конвертирует YCoCgR32 → Gray16 (Y масштабируется в 16-bit).
    /// </summary>
    /// <remarks>
    /// Value = Y * 257 = (Y &lt;&lt; 8) | Y — идеальное масштабирование 8→16.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray16 ToGray16() => new((ushort)(Y * 257));

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Gray16 → YCoCgR32.</summary>
    public static void FromGray16(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination) =>
        FromGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromGray16(
        ReadOnlySpan<Gray16> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromGray16Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Gray16.</summary>
    public static void ToGray16(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination) =>
        ToGray16(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Gray16 с явным ускорителем.</summary>
    public static unsafe void ToGray16(
        ReadOnlySpan<YCoCgR32> source,
        Span<Gray16> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray16Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
            {
                ToGray16Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        ToGray16Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray16Core(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                FromGray16Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromGray16Sse41(source, destination);
                break;
            default:
                FromGray16Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray16Core(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToGray16Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToGray16Sse41(source, destination);
                break;
            default:
                ToGray16Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromGray16Parallel(Gray16* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromGray16Core(new ReadOnlySpan<Gray16>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToGray16Parallel(YCoCgR32* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToGray16Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromGray16Scalar(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray16(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray16Scalar(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToGray16();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Gray16 → YCoCgR32.
    /// 8 пикселей за итерацию (16 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray16Sse41(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var neutral127 = YCoCgR32Sse41Vectors.ZeroCoHigh;
            var neutral3 = YCoCgR32Sse41Vectors.ZeroFrac;
            var scale255 = YCoCgR32Sse41Vectors.Scale255UShort;

            while (i + 8 <= count)
            {
                // Загружаем 8 ushort (16 байт)
                var gray16 = Sse2.LoadVector128((ushort*)(src + (i * 2)));

                // Y = (gray * 255 + 128) >> 8 — упрощённая версия
                // Точнее: (gray * 255 + 32768) >> 16, но это эквивалентно high byte
                var scaled = Sse2.MultiplyHigh(gray16, scale255);
                var yBytes = Sse2.PackUnsignedSaturate(scaled.AsInt16(), scaled.AsInt16());

                // Теперь yBytes содержит 8 Y байт в младших 8 байтах
                // Interleave с нейтральными значениями
                var yCo = Sse2.UnpackLow(yBytes, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);

                var result0 = Sse2.UnpackLow(yCo.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo.AsInt16(), cgFr.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());

                i += 8;
            }

            while (i + 4 <= count)
            {
                var gray16 = Sse2.LoadVector128((ushort*)(src + (i * 2)));
                var scaled = Sse2.MultiplyHigh(gray16, scale255);
                var yBytes = Sse2.PackUnsignedSaturate(scaled.AsInt16(), scaled.AsInt16());

                var yCo = Sse2.UnpackLow(yBytes, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);
                var result = Sse2.UnpackLow(yCo.AsInt16(), cgFr.AsInt16());

                Sse2.Store(dst + (i * 4), result.AsByte());
                i += 4;
            }

            while (i < count)
            {
                destination[i] = FromGray16(source[i]);
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
    private static unsafe void ToGray16Sse41(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Shuffle для извлечения Y
            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToYCompact;

            while (i + 8 <= count)
            {
                var v0 = Sse2.LoadVector128(src + (i * 4));
                var v1 = Sse2.LoadVector128(src + (i * 4) + 16);

                var y0 = Ssse3.Shuffle(v0, shuffleY);
                var y1 = Ssse3.Shuffle(v1, shuffleY);

                // y0: Y0 Y1 Y2 Y3 0 0 0 0 ...
                // y1: Y4 Y5 Y6 Y7 0 0 0 0 ...
                // Объединяем в один вектор
                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());
                // y01: Y0Y1Y2Y3 Y4Y5Y6Y7 0 0

                // Расширяем в 16-bit: Y * 257 = (Y << 8) | Y
                var yBytes = y01.AsByte();
                var gray16 = Sse2.UnpackLow(yBytes, yBytes);  // Y0 Y0 Y1 Y1 Y2 Y2 Y3 Y3 Y4 Y4 Y5 Y5 Y6 Y6 Y7 Y7

                Sse2.Store(dst + i, gray16.AsUInt16());

                i += 8;
            }

            while (i + 4 <= count)
            {
                var v = Sse2.LoadVector128(src + (i * 4));
                var y = Ssse3.Shuffle(v, shuffleY);
                var yLo = Sse2.UnpackLow(y, y);

                Unsafe.WriteUnaligned(dst + i, yLo.AsUInt64().GetElement(0));
                i += 4;
            }

            while (i < count)
            {
                destination[i] = source[i].ToGray16();
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
    private static unsafe void FromGray16Avx2(ReadOnlySpan<Gray16> source, Span<YCoCgR32> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var neutral127 = YCoCgR32Sse41Vectors.ZeroCoHigh;
            var neutral3 = YCoCgR32Sse41Vectors.ZeroFrac;
            var scale255 = YCoCgR32Sse41Vectors.Scale255UShort;

            while (i + 16 <= count)
            {
                // Загружаем 16 ushort (32 байт) как 2x Vector128
                var gray16Lo = Sse2.LoadVector128((ushort*)(src + (i * 2)));
                var gray16Hi = Sse2.LoadVector128((ushort*)(src + (i * 2) + 16));

                // Y = high byte of (gray * 255)
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
                FromGray16Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromGray16(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Gray16.
    /// 16 пикселей за итерацию (64 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray16Avx2(ReadOnlySpan<YCoCgR32> source, Span<Gray16> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var i = 0;
            var count = source.Length;

            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToYCompact;

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

                // Объединяем
                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());
                var y23 = Sse2.UnpackLow(y2.AsUInt32(), y3.AsUInt32());

                // y01: Y0-3 Y4-7 0 0, y23: Y8-11 Y12-15 0 0
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
                ToGray16Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = source[i].ToGray16();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Gray16 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Gray16 gray) => FromGray16(gray);

    /// <summary>Явная конвертация YCoCgR32 → Gray16.</summary>
    public static explicit operator Gray16(YCoCgR32 ycocg) => ycocg.ToGray16();

    #endregion
}
