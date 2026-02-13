#pragma warning disable CA1000, CA2208, CS1591, IDE0004, IDE0022, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Gray8 ↔ Cmyk.
/// </summary>
public readonly partial struct Gray8
{
    #region SIMD Constants

    /// <summary>
    /// Реализованные ускорители для Gray8↔CMYK.
    /// </summary>
    private const HardwareAcceleration CmykImplemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    #endregion

    #region Single Pixel Conversion

    /// <summary>
    /// Конвертирует Cmyk в Gray8.
    /// Инвертированная логика ToCmyk: Gray = 255 - K.
    /// C, M, Y игнорируются — для Gray важен только K (black).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Gray8 FromCmyk(Cmyk cmyk) => new((byte)(255 - cmyk.K));

    /// <summary>Конвертирует Gray8 в Cmyk (C = M = Y = 0, K = 255 - Value).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Cmyk ToCmyk() => new(0, 0, 0, (byte)(255 - Value));

    #endregion

    #region Batch Conversion (Gray8 → Cmyk)

    /// <summary>Пакетная конвертация Gray8 → Cmyk.</summary>
    public static void ToCmyk(ReadOnlySpan<Gray8> source, Span<Cmyk> destination) =>
        ToCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Gray8 → Cmyk с явным указанием ускорителя.</summary>
    public static unsafe void ToCmyk(ReadOnlySpan<Gray8> source, Span<Cmyk> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, CmykImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Gray8* srcPtr = source)
            fixed (Cmyk* dstPtr = destination)
                ToCmykParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        ToCmykCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ToCmykCore(ReadOnlySpan<Gray8> source, Span<Cmyk> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                ToCmykAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                ToCmykSse41(source, destination);
                break;
            default:
                ToCmykScalar(source, destination);
                break;
        }
    }

    private static unsafe void ToCmykParallel(Gray8* source, Cmyk* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            ToCmykCore(new ReadOnlySpan<Gray8>(source + start, size), new Span<Cmyk>(destination + start, size), selected);
        });
    }

    #endregion

    #region Batch Conversion (Cmyk → Gray8)

    /// <summary>Пакетная конвертация Cmyk → Gray8.</summary>
    public static void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Gray8> destination) =>
        FromCmyk(source, destination, HardwareAcceleration.Auto);

    /// <summary>Пакетная конвертация Cmyk → Gray8 с явным указанием ускорителя.</summary>
    public static unsafe void FromCmyk(ReadOnlySpan<Cmyk> source, Span<Gray8> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, CmykImplemented, source.Length);

        if (ColorSpaceHelper.ShouldParallelize(source.Length))
        {
            fixed (Cmyk* srcPtr = source)
            fixed (Gray8* dstPtr = destination)
                FromCmykParallel(srcPtr, dstPtr, source.Length, selected);
            return;
        }

        FromCmykCore(source, destination, selected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FromCmykCore(ReadOnlySpan<Cmyk> source, Span<Gray8> destination, HardwareAcceleration selected)
    {
        switch (selected)
        {
            case HardwareAcceleration.Avx2 when source.Length >= 32:
                FromCmykAvx2(source, destination);
                break;
            case HardwareAcceleration.Sse41 when source.Length >= 16:
                FromCmykSse41(source, destination);
                break;
            default:
                FromCmykScalar(source, destination);
                break;
        }
    }

    private static unsafe void FromCmykParallel(Cmyk* source, Gray8* destination, int length, HardwareAcceleration selected)
    {
        var threadCount = IColorSpace<Gray8>.GetOptimalThreadCount(length);
        var chunkSize = length / threadCount;
        var remainder = length % threadCount;

        Parallel.For(0, threadCount, i =>
        {
            var start = (i * chunkSize) + Math.Min(i, remainder);
            var size = chunkSize + (i < remainder ? 1 : 0);
            FromCmykCore(new ReadOnlySpan<Cmyk>(source + start, size), new Span<Gray8>(destination + start, size), selected);
        });
    }

    #endregion

    #region Scalar Implementation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToCmykScalar(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = (*src++).ToCmyk();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromCmykScalar(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = srcPtr;
            var dst = dstPtr;
            var end = src + source.Length;
            while (src < end)
                *dst++ = FromCmyk(*src++);
        }
    }

    #endregion

    #region SSE41 Implementation (Gray8 → Cmyk)

    /// <summary>
    /// SSE41: Gray8 → Cmyk.
    /// Gray → (C=0, M=0, Y=0, K=255-Gray).
    /// 32 пикселя за итерацию для лучшей амортизации.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToCmykSse41(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы
            var allFF = Gray8Sse41Vectors.AllFF;
            var shuffle0 = Gray8Sse41Vectors.ShuffleGrayToCmyk0;
            var shuffle1 = Gray8Sse41Vectors.ShuffleGrayToCmyk1;
            var shuffle2 = Gray8Sse41Vectors.ShuffleGrayToCmyk2;
            var shuffle3 = Gray8Sse41Vectors.ShuffleGrayToCmyk3;

            // === 32 пикселя за итерацию (две партии по 16) ===
            while (count >= 32)
            {
                // === Первые 16 пикселей ===
                var gray0 = Sse2.LoadVector128(src);
                var k0 = Sse2.Subtract(allFF, gray0);

                var cmyk0 = Ssse3.Shuffle(k0, shuffle0);
                var cmyk1 = Ssse3.Shuffle(k0, shuffle1);
                var cmyk2 = Ssse3.Shuffle(k0, shuffle2);
                var cmyk3 = Ssse3.Shuffle(k0, shuffle3);

                Sse2.Store(dst, cmyk0);
                Sse2.Store(dst + 16, cmyk1);
                Sse2.Store(dst + 32, cmyk2);
                Sse2.Store(dst + 48, cmyk3);

                // === Вторые 16 пикселей ===
                var gray1 = Sse2.LoadVector128(src + 16);
                var k1 = Sse2.Subtract(allFF, gray1);

                var cmyk4 = Ssse3.Shuffle(k1, shuffle0);
                var cmyk5 = Ssse3.Shuffle(k1, shuffle1);
                var cmyk6 = Ssse3.Shuffle(k1, shuffle2);
                var cmyk7 = Ssse3.Shuffle(k1, shuffle3);

                Sse2.Store(dst + 64, cmyk4);
                Sse2.Store(dst + 80, cmyk5);
                Sse2.Store(dst + 96, cmyk6);
                Sse2.Store(dst + 112, cmyk7);

                src += 32;
                dst += 128;
                count -= 32;
            }

            // === 16 пикселей ===
            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var k = Sse2.Subtract(allFF, gray);

                Sse2.Store(dst, Ssse3.Shuffle(k, shuffle0));
                Sse2.Store(dst + 16, Ssse3.Shuffle(k, shuffle1));
                Sse2.Store(dst + 32, Ssse3.Shuffle(k, shuffle2));
                Sse2.Store(dst + 48, Ssse3.Shuffle(k, shuffle3));

                src += 16;
                dst += 64;
                count -= 16;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Gray8 → Cmyk)

    /// <summary>
    /// AVX2: Gray8 → Cmyk.
    /// 32 пикселя за итерацию (32 байта Gray → 128 байт CMYK).
    /// Полностью 256-bit операции: AVX2 load/subtract/shuffle/store.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void ToCmykAvx2(ReadOnlySpan<Gray8> source, Span<Cmyk> destination)
    {
        fixed (Gray8* srcPtr = source)
        fixed (Cmyk* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные 256-bit константы
            var allFF = Gray8Avx2Vectors.AllFF;
            var shuffle0 = Gray8Avx2Vectors.ShuffleGrayToCmyk0;
            var shuffle1 = Gray8Avx2Vectors.ShuffleGrayToCmyk1;

            // === 32 пикселя за итерацию ===
            // Загружаем 32 байта Gray, получаем 128 байт CMYK
            while (count >= 32)
            {
                // AVX2 256-bit load: 32 Gray
                var gray = Avx.LoadVector256(src);

                // K = 255 - Gray (256-bit subtract)
                var k = Avx2.Subtract(allFF, gray);

                // k = [K0..K15 | K16..K31] (32 байта K)
                // Нужно расширить в 128 байт CMYK: каждый K → [0,0,0,K]

                // Используем VPSHUFB in-lane:
                // Lane0: K0-K15, Lane1: K16-K31
                // shuffle0: берёт K[0-3] из каждой lane → CMYK для 4 пикселей
                // shuffle1: берёт K[4-7] из каждой lane → CMYK для 4 пикселей

                // Первые 8 пикселей: K0-K3 (lane0), K16-K19 (lane1)
                var cmyk0 = Avx2.Shuffle(k, shuffle0);

                // Вторые 8 пикселей: K4-K7 (lane0), K20-K23 (lane1)
                var cmyk1 = Avx2.Shuffle(k, shuffle1);

                // Для K8-K15 и K24-K31 нужно сдвинуть данные
                // Сдвигаем K на 8 байт вправо в каждой lane
                var kShifted = Avx2.ShiftRightLogical128BitLane(k, 8);

                // K8-K11 (lane0), K24-K27 (lane1)
                var cmyk2 = Avx2.Shuffle(kShifted, shuffle0);

                // K12-K15 (lane0), K28-K31 (lane1)
                var cmyk3 = Avx2.Shuffle(kShifted, shuffle1);

                // Записываем 128 байт (4 × 256-bit stores)
                // cmyk0: пиксели 0-3 (lo) и 16-19 (hi)
                // cmyk1: пиксели 4-7 (lo) и 20-23 (hi)
                // cmyk2: пиксели 8-11 (lo) и 24-27 (hi)
                // cmyk3: пиксели 12-15 (lo) и 28-31 (hi)

                // Нужно переставить lanes для правильного порядка
                // Пиксели 0-7: cmyk0.lo + cmyk1.lo
                // Пиксели 8-15: cmyk2.lo + cmyk3.lo
                // Пиксели 16-23: cmyk0.hi + cmyk1.hi
                // Пиксели 24-31: cmyk2.hi + cmyk3.hi

                var out0 = Avx2.Permute2x128(cmyk0, cmyk1, 0x20); // lo lanes: 0-3 + 4-7
                var out1 = Avx2.Permute2x128(cmyk2, cmyk3, 0x20); // lo lanes: 8-11 + 12-15
                var out2 = Avx2.Permute2x128(cmyk0, cmyk1, 0x31); // hi lanes: 16-19 + 20-23
                var out3 = Avx2.Permute2x128(cmyk2, cmyk3, 0x31); // hi lanes: 24-27 + 28-31

                Avx.Store(dst, out0);
                Avx.Store(dst + 32, out1);
                Avx.Store(dst + 64, out2);
                Avx.Store(dst + 96, out3);

                src += 32;
                dst += 128;
                count -= 32;
            }

            // 16 пикселей fallback (SSE)
            var allFF128 = Gray8Sse41Vectors.AllFF;
            var shuffle0_128 = Gray8Sse41Vectors.ShuffleGrayToCmyk0;
            var shuffle1_128 = Gray8Sse41Vectors.ShuffleGrayToCmyk1;
            var shuffle2_128 = Gray8Sse41Vectors.ShuffleGrayToCmyk2;
            var shuffle3_128 = Gray8Sse41Vectors.ShuffleGrayToCmyk3;

            while (count >= 16)
            {
                var gray = Sse2.LoadVector128(src);
                var k = Sse2.Subtract(allFF128, gray);

                Sse2.Store(dst, Ssse3.Shuffle(k, shuffle0_128));
                Sse2.Store(dst + 16, Ssse3.Shuffle(k, shuffle1_128));
                Sse2.Store(dst + 32, Ssse3.Shuffle(k, shuffle2_128));
                Sse2.Store(dst + 48, Ssse3.Shuffle(k, shuffle3_128));

                src += 16;
                dst += 64;
                count -= 16;
            }

            // Scalar остаток
            while (count > 0)
            {
                var v = *src++;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = 0;
                *dst++ = (byte)(255 - v);
                count--;
            }
        }
    }

    #endregion


    #region SSE41 Implementation (Cmyk → Gray8)

    /// <summary>
    /// SSE41: Cmyk → Gray8.
    /// Gray = 255 - K.
    /// Or-based сборка K из 4 регистров (быстрее UnpackLow).
    /// 32 пикселя за итерацию для лучшей амортизации.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromCmykSse41(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // Кешированные константы
            var allFF = Gray8Sse41Vectors.AllFF;
            var shuffleK0 = Gray8Sse41Vectors.ShuffleCmykToK_Pos0;
            var shuffleK1 = Gray8Sse41Vectors.ShuffleCmykToK_Pos1;
            var shuffleK2 = Gray8Sse41Vectors.ShuffleCmykToK_Pos2;
            var shuffleK3 = Gray8Sse41Vectors.ShuffleCmykToK_Pos3;

            // === 32 пикселя за итерацию (две партии по 16) ===
            while (count >= 32)
            {
                // === Первые 16 пикселей ===
                var cmyk0 = Sse2.LoadVector128(src);
                var cmyk1 = Sse2.LoadVector128(src + 16);
                var cmyk2 = Sse2.LoadVector128(src + 32);
                var cmyk3 = Sse2.LoadVector128(src + 48);

                // Or-based сборка K (быстрее чем UnpackLow цепочка)
                var k0 = Ssse3.Shuffle(cmyk0, shuffleK0);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK1);
                var k2 = Ssse3.Shuffle(cmyk2, shuffleK2);
                var k3 = Ssse3.Shuffle(cmyk3, shuffleK3);

                var k01 = Sse2.Or(k0, k1);
                var k23 = Sse2.Or(k2, k3);
                var kAll0 = Sse2.Or(k01, k23);
                var gray0 = Sse2.Subtract(allFF, kAll0);

                // === Вторые 16 пикселей ===
                var cmyk4 = Sse2.LoadVector128(src + 64);
                var cmyk5 = Sse2.LoadVector128(src + 80);
                var cmyk6 = Sse2.LoadVector128(src + 96);
                var cmyk7 = Sse2.LoadVector128(src + 112);

                var k4 = Ssse3.Shuffle(cmyk4, shuffleK0);
                var k5 = Ssse3.Shuffle(cmyk5, shuffleK1);
                var k6 = Ssse3.Shuffle(cmyk6, shuffleK2);
                var k7 = Ssse3.Shuffle(cmyk7, shuffleK3);

                var k45 = Sse2.Or(k4, k5);
                var k67 = Sse2.Or(k6, k7);
                var kAll1 = Sse2.Or(k45, k67);
                var gray1 = Sse2.Subtract(allFF, kAll1);

                // Запись 32 байт
                Sse2.Store(dst, gray0);
                Sse2.Store(dst + 16, gray1);

                src += 128;
                dst += 32;
                count -= 32;
            }

            // === 16 пикселей ===
            while (count >= 16)
            {
                var cmyk0 = Sse2.LoadVector128(src);
                var cmyk1 = Sse2.LoadVector128(src + 16);
                var cmyk2 = Sse2.LoadVector128(src + 32);
                var cmyk3 = Sse2.LoadVector128(src + 48);

                var k0 = Ssse3.Shuffle(cmyk0, shuffleK0);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK1);
                var k2 = Ssse3.Shuffle(cmyk2, shuffleK2);
                var k3 = Ssse3.Shuffle(cmyk3, shuffleK3);

                var k01 = Sse2.Or(k0, k1);
                var k23 = Sse2.Or(k2, k3);
                var kAll = Sse2.Or(k01, k23);

                var gray = Sse2.Subtract(allFF, kAll);
                Sse2.Store(dst, gray);

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            var shuffleK = Gray8Sse41Vectors.ShuffleCmykToK;
            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);
                var k = Ssse3.Shuffle(cmyk, shuffleK);
                var gray = Sse2.Subtract(allFF, k);

                *(uint*)dst = gray.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                *dst++ = (byte)(255 - src[3]);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region AVX2 Implementation (Cmyk → Gray8)

    /// <summary>
    /// AVX2: Cmyk → Gray8.
    /// 32 пикселя за итерацию с SSE Or-based сборкой + 2× SSE stores.
    /// Для Backward (4:1 сжатие) SSE Or-based эффективнее AVX2 cross-lane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void FromCmykAvx2(ReadOnlySpan<Cmyk> source, Span<Gray8> destination)
    {
        fixed (Cmyk* srcPtr = source)
        fixed (Gray8* dstPtr = destination)
        {
            var src = (byte*)srcPtr;
            var dst = (byte*)dstPtr;
            var count = source.Length;

            // SSE маски для Or-based сборки K
            var allFF128 = Gray8Sse41Vectors.AllFF;
            var shuffleK0 = Gray8Sse41Vectors.ShuffleCmykToK_Pos0;
            var shuffleK1 = Gray8Sse41Vectors.ShuffleCmykToK_Pos1;
            var shuffleK2 = Gray8Sse41Vectors.ShuffleCmykToK_Pos2;
            var shuffleK3 = Gray8Sse41Vectors.ShuffleCmykToK_Pos3;

            // === 32 пикселя за итерацию (2× SSE Or-based) ===
            while (count >= 32)
            {
                // === Первые 16 пикселей ===
                var cmyk0 = Sse2.LoadVector128(src);
                var cmyk1 = Sse2.LoadVector128(src + 16);
                var cmyk2 = Sse2.LoadVector128(src + 32);
                var cmyk3 = Sse2.LoadVector128(src + 48);

                var k0 = Ssse3.Shuffle(cmyk0, shuffleK0);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK1);
                var k2 = Ssse3.Shuffle(cmyk2, shuffleK2);
                var k3 = Ssse3.Shuffle(cmyk3, shuffleK3);

                var k01_a = Sse2.Or(k0, k1);
                var k23_a = Sse2.Or(k2, k3);
                var kAll_a = Sse2.Or(k01_a, k23_a);
                var gray_a = Sse2.Subtract(allFF128, kAll_a);
                Sse2.Store(dst, gray_a);

                // === Вторые 16 пикселей ===
                var cmyk4 = Sse2.LoadVector128(src + 64);
                var cmyk5 = Sse2.LoadVector128(src + 80);
                var cmyk6 = Sse2.LoadVector128(src + 96);
                var cmyk7 = Sse2.LoadVector128(src + 112);

                var k4 = Ssse3.Shuffle(cmyk4, shuffleK0);
                var k5 = Ssse3.Shuffle(cmyk5, shuffleK1);
                var k6 = Ssse3.Shuffle(cmyk6, shuffleK2);
                var k7 = Ssse3.Shuffle(cmyk7, shuffleK3);

                var k45 = Sse2.Or(k4, k5);
                var k67 = Sse2.Or(k6, k7);
                var kAll_b = Sse2.Or(k45, k67);
                var gray_b = Sse2.Subtract(allFF128, kAll_b);
                Sse2.Store(dst + 16, gray_b);

                src += 128;
                dst += 32;
                count -= 32;
            }

            // 16 пикселей fallback (SSE с Or-based сборкой)
            // allFF128, shuffleK0-K3 уже объявлены выше
            var shuffleK0_128 = Gray8Sse41Vectors.ShuffleCmykToK_Pos0;
            var shuffleK1_128 = Gray8Sse41Vectors.ShuffleCmykToK_Pos1;
            var shuffleK2_128 = Gray8Sse41Vectors.ShuffleCmykToK_Pos2;
            var shuffleK3_128 = Gray8Sse41Vectors.ShuffleCmykToK_Pos3;

            while (count >= 16)
            {
                var cmyk0 = Sse2.LoadVector128(src);
                var cmyk1 = Sse2.LoadVector128(src + 16);
                var cmyk2 = Sse2.LoadVector128(src + 32);
                var cmyk3 = Sse2.LoadVector128(src + 48);

                var k0 = Ssse3.Shuffle(cmyk0, shuffleK0_128);
                var k1 = Ssse3.Shuffle(cmyk1, shuffleK1_128);
                var k2 = Ssse3.Shuffle(cmyk2, shuffleK2_128);
                var k3 = Ssse3.Shuffle(cmyk3, shuffleK3_128);

                var k01 = Sse2.Or(k0, k1);
                var k23 = Sse2.Or(k2, k3);
                var kAllSse = Sse2.Or(k01, k23);

                var graySse = Sse2.Subtract(allFF128, kAllSse);
                Sse2.Store(dst, graySse);

                src += 64;
                dst += 16;
                count -= 16;
            }

            // 4 пикселя fallback
            var shuffleK_128 = Gray8Sse41Vectors.ShuffleCmykToK;
            while (count >= 4)
            {
                var cmyk = Sse2.LoadVector128(src);
                var k = Ssse3.Shuffle(cmyk, shuffleK_128);
                var graySse = Sse2.Subtract(allFF128, k);

                *(uint*)dst = graySse.AsUInt32().GetElement(0);

                src += 16;
                dst += 4;
                count -= 4;
            }

            // Scalar остаток
            while (count > 0)
            {
                *dst++ = (byte)(255 - src[3]);
                src += 4;
                count--;
            }
        }
    }

    #endregion

    #region Conversion Operators

    public static explicit operator Gray8(Cmyk cmyk) => FromCmyk(cmyk);
    public static explicit operator Cmyk(Gray8 gray) => gray.ToCmyk();

    #endregion
}
