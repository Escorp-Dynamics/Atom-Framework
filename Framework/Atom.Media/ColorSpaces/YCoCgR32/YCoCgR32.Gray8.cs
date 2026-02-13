#pragma warning disable CA1000, CA2208, IDE0004, IDE0048, IDE0060, MA0051, S3776, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация YCoCgR32 ↔ Gray8.
/// Gray8 использует только компонент Y (luma) из YCoCgR32.
/// </summary>
public readonly partial struct YCoCgR32
{
    #region SIMD Constants

    /// <summary>Поддерживаемые ускорители для Gray8.</summary>
    private const HardwareAcceleration Gray8Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Gray8 → YCoCgR32 (Y=gray, Co=0, Cg=0).
    /// </summary>
    /// <remarks>
    /// Для grayscale: R = G = B = gray.
    /// Co = R - B = 0, Cg = G - t = 0.
    /// Упаковка: CoShifted = 0 + 255 = 255, CoHigh = 127, CoLsb = 1.
    /// CgShifted = 0 + 255 = 255, CgHigh = 127, CgLsb = 1.
    /// Frac = 1 | 2 = 3.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static YCoCgR32 FromGray8(Gray8 gray) =>
        new(gray.Value, 127, 127, 3);

    /// <summary>
    /// Конвертирует YCoCgR32 → Gray8 (использует только Y).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Gray8 ToGray8() => new(Y);

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Gray8 → YCoCgR32.</summary>
    public static void FromGray8(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination) =>
        FromGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → YCoCgR32 с явным ускорителем.</summary>
    public static unsafe void FromGray8(
        ReadOnlySpan<Gray8> source,
        Span<YCoCgR32> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (YCoCgR32* dstPtr = destination)
            {
                FromGray8Parallel(srcPtr, dstPtr, source.Length, selected);
            }

            return;
        }

        FromGray8Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация YCoCgR32 → Gray8.</summary>
    public static void ToGray8(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination) =>
        ToGray8(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCoCgR32 → Gray8 с явным ускорителем.</summary>
    public static unsafe void ToGray8(
        ReadOnlySpan<YCoCgR32> source,
        Span<Gray8> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        if (destination.Length < source.Length)
            ThrowDestinationTooShort();

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Gray8Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCoCgR32* srcPtr = source)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromGray8Core(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromGray8Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                FromGray8Sse41(source, destination);
                break;
            default:
                FromGray8Scalar(source, destination);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToGray8Core(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                ToGray8Avx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 4:
                ToGray8Sse41(source, destination);
                break;
            default:
                ToGray8Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromGray8Parallel(Gray8* source, YCoCgR32* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            FromGray8Core(new ReadOnlySpan<Gray8>(source + start, size), new Span<YCoCgR32>(destination + start, size), selected);
        });
    }

    private static unsafe void ToGray8Parallel(YCoCgR32* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var perThread = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * perThread) + Math.Min(i, remainder);
            var size = perThread + (i < remainder ? 1 : 0);
            ToGray8Core(new ReadOnlySpan<YCoCgR32>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromGray8Scalar(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromGray8(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToGray8Scalar(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToGray8();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Gray8 → YCoCgR32.
    /// 16 пикселей за итерацию (16 байт → 64 байт).
    /// Co=0, Cg=0 закодированы как CoHigh=127, CgHigh=127, Frac=3.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromGray8Sse41(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Нейтральные значения для Co=0, Cg=0: CoH=127, CgH=127, Frac=3
            var neutral127 = YCoCgR32Sse41Vectors.ZeroCoHigh;
            var neutral3 = YCoCgR32Sse41Vectors.ZeroFrac;

            // Обрабатываем 16 пикселей за итерацию
            while (i + 16 <= count)
            {
                // Загрузка 16 Gray8 байт
                var gray = Sse2.LoadVector128(src + i);

                // Interleave: Y_CoH и CgH_Frac
                // gray: Y0 Y1 Y2 Y3 Y4 Y5 Y6 Y7 Y8 Y9 Y10 Y11 Y12 Y13 Y14 Y15
                var yCo0 = Sse2.UnpackLow(gray, neutral127);  // Y0 127 Y1 127 Y2 127 Y3 127 Y4 127 Y5 127 Y6 127 Y7 127
                var yCo1 = Sse2.UnpackHigh(gray, neutral127); // Y8 127 Y9 127 ...
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);  // 127 3 127 3 127 3 ...
                var cgFr2 = Sse2.UnpackHigh(neutral127, neutral3);

                // Теперь нужно объединить yCo и cgFr → [Y CoH CgH Frac]
                // yCo0: Y0 CoH Y1 CoH Y2 CoH Y3 CoH Y4 CoH Y5 CoH Y6 CoH Y7 CoH
                // cgFr: CgH Frac CgH Frac ...
                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgFr.AsInt16());   // пиксели 0-3
                var result1 = Sse2.UnpackHigh(yCo0.AsInt16(), cgFr.AsInt16());  // пиксели 4-7
                var result2 = Sse2.UnpackLow(yCo1.AsInt16(), cgFr2.AsInt16());  // пиксели 8-11
                var result3 = Sse2.UnpackHigh(yCo1.AsInt16(), cgFr2.AsInt16()); // пиксели 12-15

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());
                Sse2.Store(dst + (i * 4) + 32, result2.AsByte());
                Sse2.Store(dst + (i * 4) + 48, result3.AsByte());

                i += 16;
            }

            // 4 пикселя
            while (i + 4 <= count)
            {
                var gray4 = Unsafe.ReadUnaligned<uint>(src + i);
                var grayVec = Vector128.CreateScalarUnsafe(gray4).AsByte();

                var yCo = Sse2.UnpackLow(grayVec, neutral127);
                var cgFr = Sse2.UnpackLow(neutral127, neutral3);
                var result = Sse2.UnpackLow(yCo.AsInt16(), cgFr.AsInt16());

                Sse2.Store(dst + (i * 4), result.AsByte());
                i += 4;
            }

            while (i < count)
            {
                destination[i] = FromGray8(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// SSE41: YCoCgR32 → Gray8.
    /// 16 пикселей за итерацию (64 байт → 16 байт).
    /// Извлекаем байт Y из каждого пикселя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Sse41(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // Shuffle для извлечения Y (первый байт каждого 4-byte пикселя)
            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;

            // 16 пикселей за итерацию
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

                // y0: Y0 Y1 Y2 Y3 0 0 0 0 0 0 0 0 0 0 0 0
                // Нужно объединить все 4 группы в один вектор 16 байт
                var y01 = Sse2.UnpackLow(y0.AsUInt32(), y1.AsUInt32()); // Y0Y1Y2Y3 Y4Y5Y6Y7 0 0
                var y23 = Sse2.UnpackLow(y2.AsUInt32(), y3.AsUInt32()); // Y8Y9... Y12... 0 0
                var yAll = Sse2.UnpackLow(y01.AsUInt64(), y23.AsUInt64());

                Sse2.Store(dst + i, yAll.AsByte());
                i += 16;
            }

            // 4 пикселя
            while (i + 4 <= count)
            {
                var v = Sse2.LoadVector128(src + (i * 4));
                var y = Ssse3.Shuffle(v, shuffleY);
                Unsafe.WriteUnaligned(dst + i, y.AsUInt32().GetElement(0));
                i += 4;
            }

            while (i < count)
            {
                destination[i] = source[i].ToGray8();
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
    private static unsafe void FromGray8Avx2(ReadOnlySpan<Gray8> source, Span<YCoCgR32> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCoCgR32* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            var neutral127 = YCoCgR32Avx2Vectors.ZeroCoHigh;
            var neutral3 = YCoCgR32Avx2Vectors.ZeroFrac;

            while (i + 32 <= count)
            {
                var gray = Avx.LoadVector256(src + i);

                // Обрабатываем каждую 128-bit lane отдельно
                var grayLo = gray.GetLower();  // 16 байт
                var grayHi = gray.GetUpper();  // 16 байт

                var neutral127_128 = neutral127.GetLower();
                var neutral3_128 = neutral3.GetLower();

                // First 16 pixels
                var yCo0 = Sse2.UnpackLow(grayLo, neutral127_128);
                var yCo1 = Sse2.UnpackHigh(grayLo, neutral127_128);
                var cgFr = Sse2.UnpackLow(neutral127_128, neutral3_128);
                var cgFr2 = Sse2.UnpackHigh(neutral127_128, neutral3_128);

                var result0 = Sse2.UnpackLow(yCo0.AsInt16(), cgFr.AsInt16());
                var result1 = Sse2.UnpackHigh(yCo0.AsInt16(), cgFr.AsInt16());
                var result2 = Sse2.UnpackLow(yCo1.AsInt16(), cgFr2.AsInt16());
                var result3 = Sse2.UnpackHigh(yCo1.AsInt16(), cgFr2.AsInt16());

                Sse2.Store(dst + (i * 4), result0.AsByte());
                Sse2.Store(dst + (i * 4) + 16, result1.AsByte());
                Sse2.Store(dst + (i * 4) + 32, result2.AsByte());
                Sse2.Store(dst + (i * 4) + 48, result3.AsByte());

                // Second 16 pixels
                var yCo2 = Sse2.UnpackLow(grayHi, neutral127_128);
                var yCo3 = Sse2.UnpackHigh(grayHi, neutral127_128);

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
                FromGray8Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = FromGray8(source[i]);
                i++;
            }
        }
    }

    /// <summary>
    /// AVX2: YCoCgR32 → Gray8.
    /// 32 пикселя за итерацию (128 байт → 32 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToGray8Avx2(ReadOnlySpan<YCoCgR32> source, Span<Gray8> destination)
    {
        fixed (YCoCgR32* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var i = 0;
            var count = source.Length;

            // AVX2 shuffle работает in-lane, поэтому обрабатываем как 8 SSE loads
            var shuffleY = YCoCgR32Sse41Vectors.ShuffleYCoCgToY;

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

                // Объединяем: каждый yN содержит 4 байта в младших позициях
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
                ToGray8Sse41(source[i..], destination[i..]);
                return;
            }

            while (i < count)
            {
                destination[i] = source[i].ToGray8();
                i++;
            }
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явная конвертация Gray8 → YCoCgR32.</summary>
    public static explicit operator YCoCgR32(Gray8 gray) => FromGray8(gray);

    /// <summary>Явная конвертация YCoCgR32 → Gray8.</summary>
    public static explicit operator Gray8(YCoCgR32 ycocg) => ycocg.ToGray8();

    #endregion
}
