#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S1854, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ YCbCr.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    private const HardwareAcceleration YCbCrImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует YCbCr в Gray8 (берём Y-компоненту).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromYCbCr(YCbCr ycbcr) => new(ycbcr.Y);

    /// <summary>Конвертирует Gray8 в YCbCr (Y = Value, Cb = Cr = 128).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public YCbCr ToYCbCr() => new(Value, 128, 128);

    #endregion

    #region Batch Conversion (Gray8 → YCbCr)

    /// <summary>Пакетная конвертация Gray8 → YCbCr.</summary>
    public static void ToYCbCr(ReadOnlySpan<Gray8> source, Span<YCbCr> destination) =>
        ToYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → YCbCr с явным указанием ускорителя.</summary>
    public static unsafe void ToYCbCr(ReadOnlySpan<Gray8> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (YCbCr* dstPtr = destination)
                ToYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToYCbCrCore(ReadOnlySpan<Gray8> source, Span<YCbCr> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToYCbCrSse41(source, destination);
                break;
            default:
                ToYCbCrScalar(source, destination);
                break;
        }
    }

    private static unsafe void ToYCbCrParallel(Gray8* source, YCbCr* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToYCbCrCore(new ReadOnlySpan<Gray8>(source + start, size), new Span<YCbCr>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (YCbCr → Gray8)

    /// <summary>Пакетная конвертация YCbCr → Gray8.</summary>
    public static void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Gray8> destination) =>
        FromYCbCr(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация YCbCr → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromYCbCr(ReadOnlySpan<YCbCr> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, YCbCrImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (YCbCr* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromYCbCrParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromYCbCrCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromYCbCrCore(ReadOnlySpan<YCbCr> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                FromYCbCrAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 8:
                FromYCbCrSse41(source, destination);
                break;
            default:
                FromYCbCrScalar(source, destination);
                break;
        }
    }

    private static unsafe void FromYCbCrParallel(YCbCr* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromYCbCrCore(new ReadOnlySpan<YCbCr>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToYCbCrScalar(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToYCbCr();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromYCbCrScalar(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromYCbCr(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → YCbCr)

    /// <summary>
    /// SSE41: Gray8 → YCbCr.
    /// Дублирует Y в YCbCr: (Y, 128, 128).
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrSse41(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Маски для создания YCbCr из Gray: Y0,128,128, Y1,128,128, ...
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;  // Y0,Y0,Y0, Y1,Y1,Y1, ...
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            // Маска для установки Cb=128, Cr=128: второй и третий байт каждого триплета
            var cbcrMask = Gray8Sse41Vectors.CbCrMask;
            var yMask = Gray8Sse41Vectors.YMask;

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);

                // Первые 8 пикселей → 24 байта YCbCr
                var lo = gray;
                var rgb0 = Ssse3.Shuffle(lo, shuffle0);  // Y дублируется в 3 байта
                var rgb1 = Ssse3.Shuffle(lo, shuffle1);

                // Заменяем дублированные Y на Y,128,128
                // yMask выбирает только Y, cbcrMask добавляет 128 для Cb/Cr
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                // Вторые 8 пикселей → 24 байта
                var hi = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(hi, shuffle2);
                var rgb3 = Ssse3.Shuffle(hi, shuffle3);

                // Объединяем части
                var cbcrMask2 = Gray8Sse41Vectors.CbCrMask2;
                var yMask2 = Gray8Sse41Vectors.YMask2;

                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));

                var cbcrMask3 = Gray8Sse41Vectors.CbCrMask3;
                var yMask3 = Gray8Sse41Vectors.YMask3;

                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));

                var out1 = Sse2.Or(rgb1, rgb2);

                var cbcrMask4 = Gray8Sse41Vectors.CbCrMask4;
                var yMask4 = Gray8Sse41Vectors.YMask4;

                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // Остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;     // Y
                *dst++ = 128;   // Cb
                *dst++ = 128;   // Cr
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → YCbCr)

    /// <summary>
    /// AVX2: Gray8 → YCbCr.
    /// 32 пикселя за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToYCbCrAvx2(ReadOnlySpan<Gray8> source, Span<YCbCr> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (YCbCr* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешируем SSE маски
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToRgb24_0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToRgb24_1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToRgb24_2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToRgb24_3;

            var cbcrMask = Gray8Sse41Vectors.CbCrMask;
            var yMask = Gray8Sse41Vectors.YMask;

            var cbcrMask2 = Gray8Sse41Vectors.CbCrMask2;
            var yMask2 = Gray8Sse41Vectors.YMask2;

            var cbcrMask3 = Gray8Sse41Vectors.CbCrMask3;
            var yMask3 = Gray8Sse41Vectors.YMask3;

            var cbcrMask4 = Gray8Sse41Vectors.CbCrMask4;
            var yMask4 = Gray8Sse41Vectors.YMask4;

            while (count >= 32)
            {
                // Загружаем 32 Gray8 пикселя
                var gray = Avx.LoadVector256(src);
                var lo = gray.GetLower();  // 0-15
                var hi = gray.GetUpper();  // 16-31

                // Первые 16 пикселей
                var lo0 = lo;
                var rgb0 = Ssse3.Shuffle(lo0, shuffle0);
                var rgb1 = Ssse3.Shuffle(lo0, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                var lo1 = Sse2.ShiftRightLogical128BitLane(lo, 8);
                var rgb2 = Ssse3.Shuffle(lo1, shuffle2);
                var rgb3 = Ssse3.Shuffle(lo1, shuffle3);
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out1 = Sse2.Or(rgb1, rgb2);
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                // Вторые 16 пикселей
                var hi0 = hi;
                var rgb4 = Ssse3.Shuffle(hi0, shuffle0);
                var rgb5 = Ssse3.Shuffle(hi0, shuffle1);
                rgb4 = Sse2.Or(Sse2.And(rgb4, yMask), Sse2.AndNot(yMask, cbcrMask));

                var hi1 = Sse2.ShiftRightLogical128BitLane(hi, 8);
                var rgb6 = Ssse3.Shuffle(hi1, shuffle2);
                var rgb7 = Ssse3.Shuffle(hi1, shuffle3);
                rgb5 = Sse2.Or(Sse2.And(rgb5, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb6 = Sse2.Or(Sse2.And(rgb6, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out2 = Sse2.Or(rgb5, rgb6);
                rgb7 = Sse2.Or(Sse2.And(rgb7, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb4.Store(dst + 48);
                out2.Store(dst + 64);
                rgb7.Store(dst + 80);

                src += 32;
                dst += 96;
                count -= 32;
            }

            // SSE41 fallback для 16+ пикселей
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var lo0 = gray;
                var rgb0 = Ssse3.Shuffle(lo0, shuffle0);
                var rgb1 = Ssse3.Shuffle(lo0, shuffle1);
                rgb0 = Sse2.Or(Sse2.And(rgb0, yMask), Sse2.AndNot(yMask, cbcrMask));

                var lo1 = Sse2.ShiftRightLogical128BitLane(gray, 8);
                var rgb2 = Ssse3.Shuffle(lo1, shuffle2);
                var rgb3 = Ssse3.Shuffle(lo1, shuffle3);
                rgb1 = Sse2.Or(Sse2.And(rgb1, yMask2), Sse2.AndNot(yMask2, cbcrMask2));
                rgb2 = Sse2.Or(Sse2.And(rgb2, yMask3), Sse2.AndNot(yMask3, cbcrMask3));
                var out1 = Sse2.Or(rgb1, rgb2);
                rgb3 = Sse2.Or(Sse2.And(rgb3, yMask4), Sse2.AndNot(yMask4, cbcrMask4));

                rgb0.Store(dst);
                out1.Store(dst + 16);
                rgb3.Store(dst + 32);

                src += 16;
                dst += 48;
                count -= 16;
            }

            // Остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = v;     // Y
                *dst++ = 128;   // Cb
                *dst++ = 128;   // Cr
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (YCbCr → Gray8)

    /// <summary>
    /// SSE41: YCbCr → Gray8.
    /// Извлекает Y из каждого триплета.
    /// 8 пикселей за итерацию (24 байта → 8 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrSse41(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Маски для извлечения Y: берём байты 0, 3, 6, 9, 12, 15 из первых 16 байт
            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            while (count >= 8)
            {
                // Загружаем 24 байта (8 YCbCr пикселей)
                var bytes0 = Sse2.LoadVector128(src);        // байты 0-15
                var bytes1 = Sse2.LoadScalarVector128((long*)(src + 16));  // байты 16-23

                // Извлекаем Y
                var y0 = Ssse3.Shuffle(bytes0, shuffleY0);  // Y0,Y1,Y2,Y3,Y4,Y5,0,0,...
                var y1 = Ssse3.Shuffle(bytes1.AsByte(), shuffleY1);  // 0,0,0,0,0,0,Y6,Y7,...

                var y = Sse2.Or(y0, y1);

                // Записываем 8 байт
                Unsafe.WriteUnaligned(dst, y.AsUInt64().GetElement(0));

                src += 24;
                dst += 8;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                *dst++ = *src;  // Y - первый байт триплета
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (YCbCr → Gray8)

    /// <summary>
    /// AVX2: YCbCr → Gray8.
    /// 16 пикселей за итерацию (48 байт → 16 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromYCbCrAvx2(ReadOnlySpan<YCbCr> source, Span<Gray8> destination)
    {
        fixed (YCbCr* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            var shuffleY0 = YCbCrSse41Vectors.ShuffleY0;
            var shuffleY1 = YCbCrSse41Vectors.ShuffleY1;

            while (count >= 16)
            {
                // Первые 8 пикселей (24 байта)
                var bytes0a = Sse2.LoadVector128(src);
                var bytes1a = Sse2.LoadScalarVector128((long*)(src + 16));
                var y0a = Ssse3.Shuffle(bytes0a, shuffleY0);
                var y1a = Ssse3.Shuffle(bytes1a.AsByte(), shuffleY1);
                var ya = Sse2.Or(y0a, y1a);

                // Вторые 8 пикселей (24 байта)
                var bytes0b = Sse2.LoadVector128(src + 24);
                var bytes1b = Sse2.LoadScalarVector128((long*)(src + 40));
                var y0b = Ssse3.Shuffle(bytes0b, shuffleY0);
                var y1b = Ssse3.Shuffle(bytes1b.AsByte(), shuffleY1);
                var yb = Sse2.Or(y0b, y1b);

                // Объединяем: ya имеет 8 байт в нижней части, yb — 8 байт в нижней части
                // Упаковываем в 16 байт
                var result = Sse2.UnpackLow(ya.AsUInt64(), yb.AsUInt64()).AsByte();

                result.Store(dst);

                src += 48;
                dst += 16;
                count -= 16;
            }

            // SSE41 fallback для 8+ пикселей
            while (count >= 8)
            {
                var bytes0 = Sse2.LoadVector128(src);
                var bytes1 = Sse2.LoadScalarVector128((long*)(src + 16));
                var y0 = Ssse3.Shuffle(bytes0, shuffleY0);
                var y1 = Ssse3.Shuffle(bytes1.AsByte(), shuffleY1);
                var y = Sse2.Or(y0, y1);

                Unsafe.WriteUnaligned(dst, y.AsUInt64().GetElement(0));

                src += 24;
                dst += 8;
                count -= 8;
            }

            // Остаток
            while (count > 0)
            {
                *dst++ = *src;
                src += 3;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray8(YCbCr ycbcr) => FromYCbCr(ycbcr);
    public static explicit operator YCbCr(Gray8 gray) => gray.ToYCbCr();

    #endregion
}
