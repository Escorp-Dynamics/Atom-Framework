#pragma warning disable CA1000, CA2208, IDE0004, MA0051, S3236, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray16 ↔ Bgr24.
/// </summary>
public readonly partial struct Gray16
{
    #region SIMD Constants

    /// <summary>Реализованные ускорители для Bgr24.</summary>
    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>Конвертирует Bgr24 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray16 FromBgr24(Bgr24 bgr)
    {
        var y8 = (byte)(((19595 * bgr.R) + (38470 * bgr.G) + (7471 * bgr.B) + 32768) >> 16);
        return new Gray16((ushort)(y8 | (y8 << 8)));
    }

    /// <summary>Конвертирует Gray16 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24()
    {
        var v = (byte)(Value >> 8);
        return new Bgr24(v, v, v);
    }

    #endregion

    #region Batch Conversion

    /// <summary>Пакетная конвертация Bgr24 → Gray16.</summary>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Gray16> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Bgr24 → Gray16 с явным ускорителем.</summary>
    public static unsafe void FromBgr24(
        ReadOnlySpan<Bgr24> source,
        Span<Gray16> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Bgr24* srcPtr = source)
            fixed (Gray16* dstPtr = destination)
            {
                FromBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        FromBgr24Core(source, destination, selected);
    }

    /// <summary>Пакетная конвертация Gray16 → Bgr24.</summary>
    public static void ToBgr24(ReadOnlySpan<Gray16> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray16 → Bgr24 с явным ускорителем.</summary>
    public static unsafe void ToBgr24(
        ReadOnlySpan<Gray16> source,
        Span<Bgr24> destination,
        HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray16* srcPtr = source)
            fixed (Bgr24* dstPtr = destination)
            {
                ToBgr24Parallel(srcPtr, dstPtr, source.Length, selected);
            }
            return;
        }

        ToBgr24Core(source, destination, selected);
    }

    #endregion

    #region Core Implementations

    private static void FromBgr24Core(
        ReadOnlySpan<Bgr24> source,
        Span<Gray16> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 8:
                unsafe
                {
                    fixed (Bgr24* src = source)
                    fixed (Gray16* dst = destination)
                        FromBgr24Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 4:
                unsafe
                {
                    fixed (Bgr24* src = source)
                    fixed (Gray16* dst = destination)
                        FromBgr24Sse41(src, dst, source.Length);
                }
                break;

            default:
                FromBgr24Scalar(source, destination);
                break;
        }
    }

    private static void ToBgr24Core(
        ReadOnlySpan<Gray16> source,
        Span<Bgr24> destination,
        HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 16:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Bgr24* dst = destination)
                        ToBgr24Avx2(src, dst, source.Length);
                }
                break;

            case HardwareAcceleration.Sse41 when source.Length >= 8:
                unsafe
                {
                    fixed (Gray16* src = source)
                    fixed (Bgr24* dst = destination)
                        ToBgr24Sse41(src, dst, source.Length);
                }
                break;

            default:
                ToBgr24Scalar(source, destination);
                break;
        }
    }

    #endregion

    #region Parallel Processing

    private static unsafe void FromBgr24Parallel(
        Bgr24* source, Gray16* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromBgr24Core(
                new ReadOnlySpan<Bgr24>(source + start, size),
                new Span<Gray16>(destination + start, size),
                selected);
        });
    }

    private static unsafe void ToBgr24Parallel(
        Gray16* source, Bgr24* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = ColorSpaceHelper.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToBgr24Core(
                new ReadOnlySpan<Gray16>(source + start, size),
                new Span<Bgr24>(destination + start, size),
                selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgr24Scalar(ReadOnlySpan<Bgr24> source, Span<Gray16> destination)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Gray16* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromBgr24(*src++);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgr24Scalar(ReadOnlySpan<Gray16> source, Span<Bgr24> destination)
    {
        fixed (Gray16* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = src++->ToBgr24();
        }
    }

    #endregion

    #region SSE41 Implementation

    /// <summary>
    /// SSE41: Bgr24 → Gray16.
    /// Y = 0.299*R + 0.587*G + 0.114*B (ITU-R BT.601).
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Sse41(Bgr24* src, Gray16* dst, int count)
    {
        var pSrc = (byte*)src;
        var pDst = (ushort*)dst;

        // Кешируем маски и коэффициенты
        var shuffleB0 = Gray8Sse41Vectors.ShuffleBgr24ToB0;
        var shuffleB1 = Gray8Sse41Vectors.ShuffleBgr24ToB1;
        var shuffleG0 = Gray8Sse41Vectors.ShuffleBgr24ToG0;
        var shuffleG1 = Gray8Sse41Vectors.ShuffleBgr24ToG1;
        var shuffleR0 = Gray8Sse41Vectors.ShuffleBgr24ToR0;
        var shuffleR1 = Gray8Sse41Vectors.ShuffleBgr24ToR1;
        var cR = Gray8Sse41Vectors.CoefficientR_Q16;
        var cG = Gray8Sse41Vectors.CoefficientG_Q16;
        var cB = Gray8Sse41Vectors.CoefficientB_Q16;
        var half = Gray8Sse41Vectors.Half;
        var mult257 = Gray16Sse41Vectors.Scale8To16;

        // 8 пикселей = 24 байта входа → 16 байт выхода
        while (count >= 8)
        {
            // Загружаем 24 байта (16 + 8)
            var bytes0 = Sse2.LoadVector128(pSrc);
            var bytes1 = Vector64.Load(pSrc + 16).ToVector128Unsafe();

            // Деинтерливинг B, G, R
            var bVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleB0), Ssse3.Shuffle(bytes1, shuffleB1));
            var gVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleG0), Ssse3.Shuffle(bytes1, shuffleG1));
            var rVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleR0), Ssse3.Shuffle(bytes1, shuffleR1));

            // Расширяем до int32 (первые 4 пикселя)
            var rLo = Sse41.ConvertToVector128Int32(rVec);
            var gLo = Sse41.ConvertToVector128Int32(gVec);
            var bLo = Sse41.ConvertToVector128Int32(bVec);

            // Y8 = (cR*R + cG*G + cB*B + half) >> 16
            var y8Lo = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR, rLo),
                    Sse41.MultiplyLow(cG, gLo)),
                    Sse41.MultiplyLow(cB, bLo)),
                half), 16);

            // Gray16 = y8 * 257
            var y16Lo = Sse41.MultiplyLow(y8Lo, mult257);

            // Расширяем до int32 (вторые 4 пикселя)
            var rHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec, 4));
            var gHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec, 4));
            var bHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec, 4));

            var y8Hi = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR, rHi),
                    Sse41.MultiplyLow(cG, gHi)),
                    Sse41.MultiplyLow(cB, bHi)),
                half), 16);

            var y16Hi = Sse41.MultiplyLow(y8Hi, mult257);

            // Упаковка int32 → ushort (8 пикселей)
            var result = Sse41.PackUnsignedSaturate(y16Lo, y16Hi);
            Sse2.Store(pDst, result);

            pSrc += 24;
            pDst += 8;
            count -= 8;
        }

        // Скаляр для остатка
        for (var i = 0; i < count; i++)
            pDst[i] = FromBgr24(((Bgr24*)pSrc)[i]).Value;
    }

    /// <summary>
    /// SSE41: Gray16 → Bgr24.
    /// 8 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Sse41(Gray16* src, Bgr24* dst, int count)
    {
        var pSrc = (byte*)src;
        var pDst = (byte*)dst;
        var i = 0;

        // Кешируем маски
        var shuffleHi = Gray16Sse41Vectors.ShuffleGray16ToRgb24Hi;
        var shuffleHi2 = Gray16Sse41Vectors.ShuffleGray16ToRgb24Hi2;

        // 8 пикселей за итерацию
        while (i + 8 <= count)
        {
            var g16 = Sse2.LoadVector128(pSrc);
            var rgb0 = Ssse3.Shuffle(g16, shuffleHi);
            var rgb1 = Ssse3.Shuffle(g16, shuffleHi2);

            Sse2.Store(pDst, rgb0);
            Unsafe.WriteUnaligned(pDst + 16, rgb1.AsUInt64().GetElement(0));

            pSrc += 16;
            pDst += 24;
            i += 8;
        }

        // Остаток
        while (i < count)
        {
            var v = (byte)(src[i].Value >> 8);
            *pDst++ = v;
            *pDst++ = v;
            *pDst++ = v;
            i++;
        }
    }

    #endregion

    #region AVX2 Implementation

    /// <summary>
    /// AVX2: Bgr24 → Gray16.
    /// Y = 0.299*R + 0.587*G + 0.114*B (ITU-R BT.601).
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromBgr24Avx2(Bgr24* src, Gray16* dst, int count)
    {
        var pSrc = (byte*)src;
        var pDst = (ushort*)dst;

        // Кешируем маски и коэффициенты
        var shuffleB0 = Gray8Sse41Vectors.ShuffleBgr24ToB0;
        var shuffleB1 = Gray8Sse41Vectors.ShuffleBgr24ToB1;
        var shuffleG0 = Gray8Sse41Vectors.ShuffleBgr24ToG0;
        var shuffleG1 = Gray8Sse41Vectors.ShuffleBgr24ToG1;
        var shuffleR0 = Gray8Sse41Vectors.ShuffleBgr24ToR0;
        var shuffleR1 = Gray8Sse41Vectors.ShuffleBgr24ToR1;
        var cR128 = Gray8Sse41Vectors.CoefficientR_Q16;
        var cG128 = Gray8Sse41Vectors.CoefficientG_Q16;
        var cB128 = Gray8Sse41Vectors.CoefficientB_Q16;
        var half128 = Gray8Sse41Vectors.Half;
        var mult257_128 = Gray16Sse41Vectors.Scale8To16;

        // AVX2 коэффициенты
        var cR256 = Gray16Avx2Vectors.CoefficientR;
        var cG256 = Gray16Avx2Vectors.CoefficientG;
        var cB256 = Gray16Avx2Vectors.CoefficientB;
        var half256 = Gray16Avx2Vectors.Half;
        var mult257_256 = Gray16Avx2Vectors.Scale8To16;

        // 16 пикселей = 48 байт входа → 32 байт выхода
        while (count >= 16)
        {
            // Загружаем 48 байт (16 + 16 + 16)
            var bytes0 = Sse2.LoadVector128(pSrc);
            var bytes1 = Sse2.LoadVector128(pSrc + 16);
            var bytes2 = Sse2.LoadVector128(pSrc + 32);

            // === Первые 8 пикселей ===
            var bytes1Lo = Vector64.Load(pSrc + 16).ToVector128Unsafe();
            var bVec0 = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleB0), Ssse3.Shuffle(bytes1Lo, shuffleB1));
            var gVec0 = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleG0), Ssse3.Shuffle(bytes1Lo, shuffleG1));
            var rVec0 = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleR0), Ssse3.Shuffle(bytes1Lo, shuffleR1));

            // === Вторые 8 пикселей ===
            var bytes1Hi = Sse2.ShiftRightLogical128BitLane(bytes1, 8);
            var bytes2Lo = Vector64.Load(pSrc + 40).ToVector128Unsafe();
            var combined1 = Sse2.Or(bytes1Hi, Sse2.ShiftLeftLogical128BitLane(bytes2, 8));
            var bVec1 = Sse2.Or(Ssse3.Shuffle(combined1, shuffleB0), Ssse3.Shuffle(bytes2Lo, shuffleB1));
            var gVec1 = Sse2.Or(Ssse3.Shuffle(combined1, shuffleG0), Ssse3.Shuffle(bytes2Lo, shuffleG1));
            var rVec1 = Sse2.Or(Ssse3.Shuffle(combined1, shuffleR0), Ssse3.Shuffle(bytes2Lo, shuffleR1));

            // Расширяем до int32 и объединяем в AVX2
            var r256_0 = Vector256.Create(Sse41.ConvertToVector128Int32(rVec0), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec0, 4)));
            var g256_0 = Vector256.Create(Sse41.ConvertToVector128Int32(gVec0), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec0, 4)));
            var b256_0 = Vector256.Create(Sse41.ConvertToVector128Int32(bVec0), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec0, 4)));

            var r256_1 = Vector256.Create(Sse41.ConvertToVector128Int32(rVec1), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec1, 4)));
            var g256_1 = Vector256.Create(Sse41.ConvertToVector128Int32(gVec1), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec1, 4)));
            var b256_1 = Vector256.Create(Sse41.ConvertToVector128Int32(bVec1), Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec1, 4)));

            // Y8 = (cR*R + cG*G + cB*B + half) >> 16
            var y8_0 = Avx2.ShiftRightArithmetic(
                Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cR256, r256_0),
                    Avx2.MultiplyLow(cG256, g256_0)),
                    Avx2.MultiplyLow(cB256, b256_0)),
                half256), 16);

            var y8_1 = Avx2.ShiftRightArithmetic(
                Avx2.Add(Avx2.Add(Avx2.Add(
                    Avx2.MultiplyLow(cR256, r256_1),
                    Avx2.MultiplyLow(cG256, g256_1)),
                    Avx2.MultiplyLow(cB256, b256_1)),
                half256), 16);

            // Gray16 = y8 * 257
            var y16_0 = Avx2.MultiplyLow(y8_0, mult257_256);
            var y16_1 = Avx2.MultiplyLow(y8_1, mult257_256);

            // Упаковываем int32 → uint16
            var packed0 = Avx2.PackUnsignedSaturate(y16_0, y16_1);
            // Permute для правильного порядка (AVX2 pack работает in-lane)
            var result = Avx2.Permute4x64(packed0.AsInt64(), 0b11_01_10_00).AsUInt16();

            // Записываем 32 байта (16 пикселей Gray16)
            Avx.Store(pDst, result);

            pSrc += 48;
            pDst += 16;
            count -= 16;
        }

        // 8 пикселей (SSE fallback)
        while (count >= 8)
        {
            var bytes0 = Sse2.LoadVector128(pSrc);
            var bytes1 = Vector64.Load(pSrc + 16).ToVector128Unsafe();

            var bVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleB0), Ssse3.Shuffle(bytes1, shuffleB1));
            var gVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleG0), Ssse3.Shuffle(bytes1, shuffleG1));
            var rVec = Sse2.Or(Ssse3.Shuffle(bytes0, shuffleR0), Ssse3.Shuffle(bytes1, shuffleR1));

            var rLo = Sse41.ConvertToVector128Int32(rVec);
            var gLo = Sse41.ConvertToVector128Int32(gVec);
            var bLo = Sse41.ConvertToVector128Int32(bVec);

            var y8Lo = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR128, rLo),
                    Sse41.MultiplyLow(cG128, gLo)),
                    Sse41.MultiplyLow(cB128, bLo)),
                half128), 16);

            var y16Lo = Sse41.MultiplyLow(y8Lo, mult257_128);

            var rHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(rVec, 4));
            var gHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(gVec, 4));
            var bHi = Sse41.ConvertToVector128Int32(Sse2.ShiftRightLogical128BitLane(bVec, 4));

            var y8Hi = Sse2.ShiftRightArithmetic(
                Sse2.Add(Sse2.Add(Sse2.Add(
                    Sse41.MultiplyLow(cR128, rHi),
                    Sse41.MultiplyLow(cG128, gHi)),
                    Sse41.MultiplyLow(cB128, bHi)),
                half128), 16);

            var y16Hi = Sse41.MultiplyLow(y8Hi, mult257_128);

            var y16 = Sse41.PackUnsignedSaturate(y16Lo, y16Hi);
            Sse2.Store(pDst, y16);

            pSrc += 24;
            pDst += 8;
            count -= 8;
        }

        // Остаток
        while (count > 0)
        {
            *pDst++ = FromBgr24(*(Bgr24*)pSrc).Value;
            pSrc += 3;
            count--;
        }
    }

    /// <summary>
    /// AVX2: Gray16 → Bgr24.
    /// 16 пикселей за итерацию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToBgr24Avx2(Gray16* src, Bgr24* dst, int count)
    {
        var pSrc = (byte*)src;
        var pDst = (byte*)dst;
        var i = 0;

        // Кешируем маски
        var shuffleHi = Gray16Sse41Vectors.ShuffleGray16ToRgb24Hi;
        var shuffleHi2 = Gray16Sse41Vectors.ShuffleGray16ToRgb24Hi2;

        // 16 пикселей за итерацию
        while (i + 16 <= count)
        {
            var g16_0 = Sse2.LoadVector128(pSrc);
            var g16_1 = Sse2.LoadVector128(pSrc + 16);

            var rgb00 = Ssse3.Shuffle(g16_0, shuffleHi);
            var rgb01 = Ssse3.Shuffle(g16_0, shuffleHi2);
            var rgb10 = Ssse3.Shuffle(g16_1, shuffleHi);
            var rgb11 = Ssse3.Shuffle(g16_1, shuffleHi2);

            Sse2.Store(pDst, rgb00);
            Unsafe.WriteUnaligned(pDst + 16, rgb01.AsUInt64().GetElement(0));
            Sse2.Store(pDst + 24, rgb10);
            Unsafe.WriteUnaligned(pDst + 40, rgb11.AsUInt64().GetElement(0));

            pSrc += 32;
            pDst += 48;
            i += 16;
        }

        // 8 пикселей (SSE fallback)
        while (i + 8 <= count)
        {
            var g16 = Sse2.LoadVector128(pSrc);
            var rgb0 = Ssse3.Shuffle(g16, shuffleHi);
            var rgb1 = Ssse3.Shuffle(g16, shuffleHi2);
            Sse2.Store(pDst, rgb0);
            Unsafe.WriteUnaligned(pDst + 16, rgb1.AsUInt64().GetElement(0));

            pSrc += 16;
            pDst += 24;
            i += 8;
        }

        // Остаток
        while (i < count)
        {
            var v = (byte)(src[i].Value >> 8);
            *pDst++ = v;
            *pDst++ = v;
            *pDst++ = v;
            i++;
        }
    }

    #endregion

    #region Conversion Operators

    /// <summary>Явное преобразование Bgr24 → Gray16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Gray16(Bgr24 bgr) => FromBgr24(bgr);

    /// <summary>Явное преобразование Gray16 → Bgr24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator Bgr24(Gray16 gray) => gray.ToBgr24();

    #endregion
}
