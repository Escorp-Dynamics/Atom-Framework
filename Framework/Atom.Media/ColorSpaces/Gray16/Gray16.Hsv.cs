#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Hsv (4-байтовый формат: H16, S8, V8).
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    private const HardwareAcceleration HsvImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Hsv в Gray16 (V × 257).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromHsv(Hsv hsv) => new((ushort)(hsv.V * 257));

    /// <summary>Конвертирует Gray16 в Hsv (H = 0, S = 0, V = Value / 257).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hsv ToHsv()
    {
        // Точное деление на 257 через Q16 fixed-point:
        // Value / 257 = (Value * 255 + 32768) >> 16
        // Это даёт LOSSLESS round-trip для V*257 значений
        var v = (byte)(((Value * 255) + 32768) >> 16);
        return new(0, 0, v);
    }

    #endregion

    #region Batch Conversion (Gray16 → Hsv)

    /// <summary>Пакетная конвертация Gray16 → Hsv.</summary>
    public static void ToHsv(ReadOnlySpan<Gray16> source, Span<Hsv> destination) =>
        ToHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Hsv с явным указанием ускорителя.</summary>
    public static unsafe void ToHsv(ReadOnlySpan<Gray16> source, Span<Hsv> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Hsv* dstPtr = destination)
                ToHsvParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToHsvCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToHsvCore(ReadOnlySpan<Gray16> source, Span<Hsv> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
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

    private static unsafe void ToHsvParallel(Gray16* source, Hsv* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToHsvCore(new ReadOnlySpan<Gray16>(source + start, size), new Span<Hsv>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Hsv → Gray16)

    /// <summary>Пакетная конвертация Hsv → Gray16.</summary>
    public static void FromHsv(ReadOnlySpan<Hsv> source, Span<Gray16> destination) =>
        FromHsv(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Hsv → Gray16 с явным указанием ускорителя.</summary>
    public static unsafe void FromHsv(ReadOnlySpan<Hsv> source, Span<Gray16> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, HsvImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Hsv* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
                FromHsvParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromHsvCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromHsvCore(ReadOnlySpan<Hsv> source, Span<Gray16> destination, HardwareAcceleration selected)
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

    private static unsafe void FromHsvParallel(Hsv* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray16>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromHsvCore(new ReadOnlySpan<Hsv>(source + start, size), new Span<Gray16>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToHsvScalar(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
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
    private static unsafe void FromHsvScalar(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromHsv(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray16 → Hsv)

    /// <summary>
    /// SSE41: Gray16 → Hsv (4-байтовый формат) с точным делением на 257.
    /// 8 пикселей за итерацию: 16 байт входа → 32 байт выхода.
    /// Gray16[V16] → Hsv[H=0, S=0, V=V16/257].
    /// Формула: V = (Value * 255 + 32768) >> 16 = LOSSLESS для V*257 значений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToHsvSse41(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16
            var mult255 = Gray16Sse41Vectors.Mult255;

            // Hsv layout: [H_lo, H_hi, S, V] = 4 байта на пиксель
            var shuffleToHsv = Gray16Sse41Vectors.ShuffleGrayToHsv;

            // 8 пикселей за итерацию (16 байт входа → 32 байт выхода)
            while (count >= 8)
            {
                // Загружаем 8 Gray16 значений
                var gray16 = Sse2.LoadVector128(src);

                // Q16 деление на 257: (gray16 * 255 + 32768) >> 16
                // = hi + carry, где carry = 1 если (lo + 32768) >= 65536
                // = hi + (lo >= 32768) = hi + (lo >> 15)
                var lo = Sse2.MultiplyLow(gray16, mult255);       // (gray16 * 255) & 0xFFFF
                var hi = Sse2.MultiplyHigh(gray16, mult255);      // (gray16 * 255) >> 16

                // Unsigned carry: lo >= 32768 эквивалентно lo >> 15
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);

                // Упаковываем 8 ushort → 8 байт
                var packed = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());

                // Первые 4 пикселя (байты 0-3)
                var hsv0 = Ssse3.Shuffle(packed, shuffleToHsv);

                // Вторые 4 пикселя (байты 4-7)
                var packed1 = Sse2.ShiftRightLogical128BitLane(packed, 4);
                var hsv1 = Ssse3.Shuffle(packed1, shuffleToHsv);

                hsv0.Store(dst);
                hsv1.Store(dst + 16);

                src += 8;
                dst += 32;
                count -= 8;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var gray16 = Sse2.LoadScalarVector128((long*)src).AsUInt16();

                var lo = Sse2.MultiplyLow(gray16, mult255);
                var hi = Sse2.MultiplyHigh(gray16, mult255);
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);

                var packed = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());
                var hsv = Ssse3.Shuffle(packed.AsByte(), shuffleToHsv);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            // Scalar остаток с точным делением
            while (count > 0)
            {
                var v = (byte)(((*src * 255) + 32768) >> 16);
                *dst++ = 0;   // H_lo
                *dst++ = 0;   // H_hi
                *dst++ = 0;   // S
                *dst++ = v;   // V
                src++;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray16 → Hsv)

    /// <summary>
    /// AVX2: Gray16 → Hsv (4-байтовый формат) с точным делением на 257.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToHsvAvx2(ReadOnlySpan<Gray16> source, Span<Hsv> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Hsv* dstPtr = destination)
        {
            var src = (ushort*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Q16 деление на 257: (x * 255 + 32768) >> 16
            var mult255_256 = Gray16Avx2Vectors.Mult255;
            var mult255_128 = Gray16Sse41Vectors.Mult255;

            var shuffleToHsv = Gray16Sse41Vectors.ShuffleGrayToHsv;

            // 16 пикселей за итерацию (32 байт входа → 64 байт выхода)
            while (count >= 16)
            {
                // Загружаем 16 Gray16 значений
                var gray16 = Avx.LoadVector256(src);

                // Q16: (gray16 * 255 + 32768) >> 16 = hi + (lo >> 15)
                var lo = Avx2.MultiplyLow(gray16, mult255_256);
                var hi = Avx2.MultiplyHigh(gray16, mult255_256);
                var carry = Avx2.ShiftRightLogical(lo, 15);
                var result = Avx2.Add(hi, carry);

                // Упаковываем в байты (AVX2 работает in-lane)
                var packed = Avx2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());

                // Распаковываем результат (PackUnsignedSaturate даёт [0-3,8-11,4-7,12-15])
                var loVec = packed.GetLower().AsByte();  // байты 0-7
                var hiVec = packed.GetUpper().AsByte();  // байты 8-15

                // Преобразуем 4 байта → 16 байт Hsv для каждой группы
                var hsv0 = Ssse3.Shuffle(loVec, shuffleToHsv);                           // pixels 0-3
                var hsv1 = Ssse3.Shuffle(Sse2.ShiftRightLogical128BitLane(loVec, 4), shuffleToHsv);  // pixels 4-7
                var hsv2 = Ssse3.Shuffle(hiVec, shuffleToHsv);                           // pixels 8-11
                var hsv3 = Ssse3.Shuffle(Sse2.ShiftRightLogical128BitLane(hiVec, 4), shuffleToHsv);  // pixels 12-15

                hsv0.Store(dst);
                hsv1.Store(dst + 16);
                hsv2.Store(dst + 32);
                hsv3.Store(dst + 48);

                src += 16;
                dst += 64;
                count -= 16;
            }

            // 8 пикселей
            while (count >= 8)
            {
                var gray16 = Sse2.LoadVector128(src);

                var lo = Sse2.MultiplyLow(gray16, mult255_128);
                var hi = Sse2.MultiplyHigh(gray16, mult255_128);
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);

                var packed = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());

                var hsv0 = Ssse3.Shuffle(packed.AsByte(), shuffleToHsv);
                var packed1 = Sse2.ShiftRightLogical128BitLane(packed, 4);
                var hsv1 = Ssse3.Shuffle(packed1.AsByte(), shuffleToHsv);

                hsv0.Store(dst);
                hsv1.Store(dst + 16);

                src += 8;
                dst += 32;
                count -= 8;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var gray16 = Sse2.LoadScalarVector128((long*)src).AsUInt16();

                var lo = Sse2.MultiplyLow(gray16, mult255_128);
                var hi = Sse2.MultiplyHigh(gray16, mult255_128);
                var carry = Sse2.ShiftRightLogical(lo, 15);
                var result = Sse2.Add(hi, carry);

                var packed = Sse2.PackUnsignedSaturate(result.AsInt16(), result.AsInt16());
                var hsv = Ssse3.Shuffle(packed.AsByte(), shuffleToHsv);
                hsv.Store(dst);

                src += 4;
                dst += 16;
                count -= 4;
            }

            // Scalar остаток с точным делением
            while (count > 0)
            {
                var v = (byte)(((*src * 255) + 32768) >> 16);
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = v;
                src++;
                count--;
            }
        }
    }

    #endregion

    #region SSE41 Implementation (Hsv → Gray16)

    /// <summary>
    /// SSE41: Hsv → Gray16 (4-байтовый формат).
    /// 4 пикселя за итерацию: 16 байт входа → 8 байт выхода.
    /// Hsv[V8] → Gray16[V8 × 257].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromHsvSse41(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            // V находится на позициях 3, 7, 11, 15
            var shuffleV = Gray16Sse41Vectors.ShuffleHsvToV;

            var mult257 = Gray16Sse41Vectors.Mult257;

            // 8 пикселей за итерацию (32 байт входа → 16 байт выхода)
            while (count >= 8)
            {
                var hsv0 = Sse2.LoadVector128(src);        // пиксели 0-3
                var hsv1 = Sse2.LoadVector128(src + 16);   // пиксели 4-7

                var v0 = Ssse3.Shuffle(hsv0, shuffleV);   // [V0,V1,V2,V3, 0...]
                var v1 = Ssse3.Shuffle(hsv1, shuffleV);   // [V4,V5,V6,V7, 0...]

                // Объединяем 8 байтов V
                var vBytes = Sse2.UnpackLow(v0.AsUInt32(), v1.AsUInt32()).AsByte();  // [V0-V3, V4-V7, 0...]

                // Конвертируем byte → short и умножаем на 257
                var vShort = Sse41.ConvertToVector128Int16(vBytes);
                var gray16 = Sse2.MultiplyLow(vShort, mult257);

                Sse2.Store(dst, gray16.AsUInt16());

                src += 32;
                dst += 8;
                count -= 8;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src);
                var vBytes = Ssse3.Shuffle(hsv, shuffleV);

                var vShort = Sse41.ConvertToVector128Int16(vBytes);
                var gray16 = Sse2.MultiplyLow(vShort, mult257);

                Unsafe.WriteUnaligned(dst, gray16.AsUInt64().GetElement(0));

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = *(src + 3);  // V - 4-й байт (индекс 3)
                *dst++ = (ushort)(v * 257);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Hsv → Gray16)

    /// <summary>
    /// AVX2: Hsv → Gray16 (4-байтовый формат).
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromHsvAvx2(ReadOnlySpan<Hsv> source, Span<Gray16> destination)
    {
        fixed (Hsv* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (ushort*)dstPtr;
            var count = source.Length;

            var shuffleV = Gray16Sse41Vectors.ShuffleHsvToV;

            var mult257 = Gray16Sse41Vectors.Mult257;

            // 8 пикселей за итерацию
            while (count >= 8)
            {
                var hsv0 = Sse2.LoadVector128(src);
                var hsv1 = Sse2.LoadVector128(src + 16);

                var v0 = Ssse3.Shuffle(hsv0, shuffleV);
                var v1 = Ssse3.Shuffle(hsv1, shuffleV);

                var vBytes = Sse2.UnpackLow(v0.AsUInt32(), v1.AsUInt32()).AsByte();
                var vShort = Sse41.ConvertToVector128Int16(vBytes);
                var gray16 = Sse2.MultiplyLow(vShort, mult257);

                Sse2.Store(dst, gray16.AsUInt16());

                src += 32;
                dst += 8;
                count -= 8;
            }

            // 4 пикселя
            while (count >= 4)
            {
                var hsv = Sse2.LoadVector128(src);
                var vBytes = Ssse3.Shuffle(hsv, shuffleV);

                var vShort = Sse41.ConvertToVector128Int16(vBytes);
                var gray16 = Sse2.MultiplyLow(vShort, mult257);

                Unsafe.WriteUnaligned(dst, gray16.AsUInt64().GetElement(0));

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = *(src + 3);
                *dst++ = (ushort)(v * 257);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray16(Hsv hsv) => FromHsv(hsv);
    public static explicit operator Hsv(Gray16 gray) => gray.ToHsv();

    #endregion
}
