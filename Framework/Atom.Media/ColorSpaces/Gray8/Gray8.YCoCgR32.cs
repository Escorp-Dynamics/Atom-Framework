#pragma warning disable CA1000, CA2208, IDE0004, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ YCoCgR32.
/// </summary>
public readonly partial struct Gray8
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
    /// Конвертирует YCoCgR32 → Gray8 (извлекает Y-компоненту).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromYCoCgR32(YCoCgR32 ycocg) => new(ycocg.Y);

    /// <summary>
    /// Конвертирует Gray8 → YCoCgR32 (Y = Value, Co = 0, Cg = 0).
    /// </summary>
    /// <remarks>
    /// Для grayscale: R = G = B = gray.
    /// Co = R - B = 0, Cg = G - t = 0.
    /// Упаковка: CoHigh = 127, CgHigh = 127, Frac = 3.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCoCgR32 ToYCoCgR32() => new(Value, 127, 127, 3);

    #endregion

    #region Batch Conversion (Gray8 → YCoCgR32)

    /// <summary>Пакетная конвертация Gray8 → YCoCgR32.</summary>
    public static void ToYCoCgR32(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination) =>
        ToYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → YCoCgR32 с явным указанием ускорителя.</summary>
    public static unsafe void ToYCoCgR32(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCoCgR32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
                ToYCoCgR32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToYCoCgR32Core(source, destination, selected);
    }

    #endregion

    #region Batch Conversion (YCoCgR32 → Gray8)

    /// <summary>Пакетная конвертация YCoCgR32 → Gray8.</summary>
    public static void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination) =>
        FromYCoCgR32(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromYCoCgR32(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCoCgR32Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromYCoCgR32Parallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromYCoCgR32Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCoCgR32Core(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToYCoCgR32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToYCoCgR32Sse41(source, destination);
                break;
            default:
                ToYCoCgR32Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCoCgR32Core(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromYCoCgR32Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                FromYCoCgR32Sse41(source, destination);
                break;
            default:
                FromYCoCgR32Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void ToYCoCgR32Parallel(Gray8* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToYCoCgR32Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void FromYCoCgR32Parallel(YCoCgR32* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromYCoCgR32Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCoCgR32Scalar(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
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
    private static unsafe void FromYCoCgR32Scalar(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
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
    /// SSE41: Gray8 → YCoCgR32.
    /// 16 пикселей за итерацию (16 байт → 64 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCoCgR32Sse41(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Нейтральные значения для Co=0, Cg=0: CoH=127, CgH=127, Frac=3
            var neutral127 = Gray8Sse41Vectors.Neutral127;
            var neutral3 = Gray8Sse41Vectors.Neutral3;

            while (i + 16 <= count)
            {
                // Загрузка 16 Gray8 байт
                var gray = Sse2.LoadVector128(src + i);

                // Interleave Y с нейтральными CoH, CgH, Frac
                var yCo0 = Sse2.UnpackLow(gray, neutral127);
                var yCo1 = Sse2.UnpackHigh(gray, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);
                var cgFr2 = Sse2.UnpackHigh(neutral127, neutral3);

                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo0.AsInt16(), cgFr.AsInt16());
                var result2 = Sse2.UnpackLow(yCo1.AsInt16(), cgFr2.AsInt16());
                var result3 = Sse2.UnpackHigh(yCo1.AsInt16(), cgFr2.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());
                Sse2.Store(dst + (i * 4) + 32, result2.AsByte());
                Sse2.Store(dst + (i * 4) + 48, result3.AsByte());

                i += 16;
            }

            while (i < count)
            {
                destination[i] = source[i].ToYCoCgR32();
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Gray8.
    /// 16 пикселей за итерацию (64 байт → 16 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCoCgR32Sse41(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Shuffle для извлечения Y (первый байт каждого 4-byte пикселя)
            var shuffleY = Gray8Sse41Vectors.ShuffleYCoCgR32ToY;

            while (i + 16 <= count)
            {
                var v0 = Sse2.LoadVector128(src + (i * 4));
                var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
                var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
                var v3 = Sse2.LoadVector128(src + (i * 4) + 48);

                var y0 = Ssse3.Shuffle(v0, shuffleY);
                var y1 = Ssse3.Shuffle(v1, shuffleY);
                var y2 = Ssse3.Shuffle(v2, shuffleY);
                var y3 = Ssse3.Shuffle(v3, shuffleY);

                // Объединяем: каждый yN имеет 4 байта в младших позициях
                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());
                var y23 = Sse2.UnpackLow(y2.AsUInt32(), y3.AsUInt32());
                var yAll = Sse2.UnpackLow(y01.AsUInt64(), y23.AsUInt64());

                Sse2.Store(dst + i, yAll.AsByte());
                i += 16;
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
    /// AVX2: Gray8 → YCoCgR32.
    /// 32 пикселя за итерацию (32 байт → 128 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCoCgR32Avx2(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var neutral127 = Gray8Sse41Vectors.Neutral127;
            var neutral3 = Gray8Sse41Vectors.Neutral3;

            while (i + 32 <= count)
            {
                var gray = Avx.LoadVector256(src + i);
                var grayLo = gray.GetLower();
                var grayHi = gray.GetUpper();

                // First 16 pixels
                var yCo0 = Sse2.UnpackLow(grayLo, neutral127);
                var yCo1 = Sse2.UnpackHigh(grayLo, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);
                var cgFr2 = Sse2.UnpackHigh(neutral127, neutral3);

                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo0.AsInt16(), cgFr.AsInt16());
                var result2 = Sse2.UnpackLow(yCo1.AsInt16(), cgFr2.AsInt16());
                var result3 = Sse2.UnpackHigh(yCo1.AsInt16(), cgFr2.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());
                Sse2.Store(dst + (i * 4) + 32, result2.AsByte());
                Sse2.Store(dst + (i * 4) + 48, result3.AsByte());

                // Second 16 pixels
                var yCo2 = Sse2.UnpackLow(grayHi, neutral127);
                var yCo3 = Sse2.UnpackHigh(grayHi, neutral127);

                var result4 = Sse2.UnpackLow(yCo2.AsInt16(), cgFr.AsInt16());
                var result5 = Sse2.UnpackHigh(yCo2.AsInt16(), cgFr.AsInt16());
                var result6 = Sse2.UnpackLow(yCo3.AsInt16(), cgFr2.AsInt16());
                var result7 = Sse2.UnpackHigh(yCo3.AsInt16(), cgFr2.AsInt16());

                Sse2.Store(dst + (i * 4) + 64, result4.AsByte());
                Sse2.Store(dst + (i * 4) + 80, result5.AsByte());
                Sse2.Store(dst + (i * 4) + 96, result6.AsByte());
                Sse2.Store(dst + (i * 4) + 112, result7.AsByte());

                i += 32;
            }

            if (i + 16 <= count)
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
    /// AVX2: YCoCgR32 → Gray8.
    /// 32 пикселя за итерацию (128 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCoCgR32Avx2(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var shuffleY = Gray8Sse41Vectors.ShuffleYCoCgR32ToY;

            while (i + 32 <= count)
            {
                // Загружаем 32 пикселя (128 байт) как 8x Vector128
                var v0 = Sse2.LoadVector128(src + (i * 4));
                var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
                var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
                var v3 = Sse2.LoadVector128(src + (i * 4) + 48);
                var v4 = Sse2.LoadVector128(src + (i * 4) + 64);
                var v5 = Sse2.LoadVector128(src + (i * 4) + 80);
                var v6 = Sse2.LoadVector128(src + (i * 4) + 96);
                var v7 = Sse2.LoadVector128(src + (i * 4) + 112);

                var y0 = Ssse3.Shuffle(v0, shuffleY);
                var y1 = Ssse3.Shuffle(v1, shuffleY);
                var y2 = Ssse3.Shuffle(v2, shuffleY);
                var y3 = Ssse3.Shuffle(v3, shuffleY);
                var y4 = Ssse3.Shuffle(v4, shuffleY);
                var y5 = Ssse3.Shuffle(v5, shuffleY);
                var y6 = Ssse3.Shuffle(v6, shuffleY);
                var y7 = Ssse3.Shuffle(v7, shuffleY);

                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32());
                var y23 = Sse2.UnpackLow(y2.AsUInt32(), y3.AsUInt32());
                var y0123 = Sse2.UnpackLow(y01.AsUInt64(), y23.AsUInt64());

                var y45 = Sse2.UnpackLow(y4.AsUInt32(), y5.AsUInt32());
                var y67 = Sse2.UnpackLow(y6.AsUInt32(), y7.AsUInt32());
                var y4567 = Sse2.UnpackLow(y45.AsUInt64(), y67.AsUInt64());

                Sse2.Store(dst + i, y0123.AsByte());
                Sse2.Store(dst + i + 16, y4567.AsByte());

                i += 32;
            }

            if (i + 16 <= count)
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

    /// <summary>Явная конвертация Gray8 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Gray8 gray) => gray.ToYCoCgR32();

    /// <summary>Явная конвертация YCoCgR32 → Gray8.</summary>
    public static explicit operator Gray8(YCoCgR32 ycocg) => FromYCoCgR32(ycocg);

    #endregion
}
