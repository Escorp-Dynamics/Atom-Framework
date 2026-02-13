#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Bgr24.
/// SIMD: только добавление/удаление альфа-канала (без swap — порядок B,G,R одинаковый).
/// </summary>
public readonly partial struct Bgra32
{
    /// <summary>
    /// Реализованные ускорители для конвертации Bgra32 ↔ Bgr24.
    /// </summary>
    private const HardwareAcceleration Bgr24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    #region Single Pixel Conversion (Bgr24)

    /// <summary>Конвертирует Bgr24 в Bgra32 (A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromBgr24(Bgr24 bgr) => new(bgr.B, bgr.G, bgr.R, 255);

    /// <summary>Конвертирует Bgra32 в Bgr24 (отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24 ToBgr24() => new(B, G, R);

    #endregion

    #region Batch Conversion (Bgra32 ↔ Bgr24)

    /// <summary>
    /// Пакетная конвертация Bgr24 → Bgra32 с SIMD.
    /// </summary>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Bgra32> destination) =>
        FromBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgr24 → Bgra32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgr24.</param>
    /// <param name="destination">Целевой буфер Bgra32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void FromBgr24(ReadOnlySpan<Bgr24> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов: SIMD использует overlapping reads
        FromBgr24Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Bgra32 → Bgr24 с SIMD.
    /// </summary>
    public static void ToBgr24(ReadOnlySpan<Bgra32> source, Span<Bgr24> destination) =>
        ToBgr24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → Bgr24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgra32.</param>
    /// <param name="destination">Целевой буфер Bgr24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void ToBgr24(ReadOnlySpan<Bgra32> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Bgr24Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов: SIMD использует overlapping reads
        ToBgr24Core(source, destination, selected);
    }

    #endregion

    #region Core SIMD (Bgr24)

    /// <summary>Однопоточная SIMD конвертация Bgr24 → Bgra32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromBgr24Core(ReadOnlySpan<Bgr24> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Bgr24ToBgra32Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    Bgr24ToBgra32Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Bgr24ToBgra32Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Bgr24ToBgra32Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Однопоточная SIMD конвертация Bgra32 → Bgr24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToBgr24Core(ReadOnlySpan<Bgra32> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Bgra32ToBgr24Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    Bgra32ToBgr24Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Bgra32ToBgr24Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Bgra32ToBgr24Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    #endregion



    #region SSSE3 Implementations (Bgr24)

    /// <summary>
    /// SSSE3: BGR24 → BGRA32 (добавить A=255, без swap).
    /// 16 пикселей за итерацию с 4x unroll.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToBgra32Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask;
        var alphaMask = Bgra32Sse41Vectors.Alpha255Mask;

        // 16 пикселей: 48 байт вход → 64 байта выход
        while (i + 16 <= pixelCount)
        {
            // Загружаем 4 блока по 12 байт (с overlap)
            var v0 = Sse2.LoadVector128(src + (i * 3));       // байты 0-15 (пиксели 0-3 + часть 4)
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);  // байты 12-27 (пиксели 4-7 + часть 8)
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);  // байты 24-39 (пиксели 8-11 + часть 12)
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);  // байты 36-51 (пиксели 12-15)

            var r0 = Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask);

            Sse2.Store(dst + (i * 4), r0);
            Sse2.Store(dst + (i * 4) + 16, r1);
            Sse2.Store(dst + (i * 4) + 32, r2);
            Sse2.Store(dst + (i * 4) + 48, r3);

            i += 16;
        }

        // 4 пикселя
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 3;
            var dstOffset = i * 4;
            dst[dstOffset] = src[srcOffset];         // B
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset + 2]; // R
            dst[dstOffset + 3] = 255;                // A
            i++;
        }
    }

    /// <summary>
    /// SSSE3: BGRA32 → BGR24 (удалить A, без swap).
    /// 16 пикселей за итерацию с 4x unroll и overlapping stores.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToBgr24Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask;

        // 16 пикселей = 64 байта вход → 48 байт выход
        // Используем overlapping 16-byte stores для минимизации операций
        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 4));
            var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
            var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
            var v3 = Sse2.LoadVector128(src + (i * 4) + 48);

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            // Overlapping stores: каждый store пишет 16 байт, но нужно только 12
            // Следующий store перезаписывает 4 "лишних" байта корректными данными
            Sse2.Store(dst + (i * 3), r0);           // байты 0-15 (нужны 0-11)
            Sse2.Store(dst + (i * 3) + 12, r1);      // байты 12-27 (нужны 12-23)
            Sse2.Store(dst + (i * 3) + 24, r2);      // байты 24-39 (нужны 24-35)
            Sse2.Store(dst + (i * 3) + 36, r3);      // байты 36-51 (нужны 36-47)

            i += 16;
        }

        // 4 пикселя
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);

            // 12 байт через две записи
            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));

            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset];         // B
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset + 2]; // R
            i++;
        }
    }

    #endregion

    #region AVX2 Implementations (Bgr24)

    /// <summary>
    /// AVX2: BGR24 → BGRA32 (добавить A=255).
    /// Использует SSE shuffle с минимальным overhead для максимальной производительности.
    /// 16 пикселей за итерацию (48 байт BGR → 64 байт BGRA).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToBgra32Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask;
        var alphaMask = Bgra32Sse41Vectors.Alpha255Mask;

        // 16 пикселей BGR = 48 байт → 64 байт BGRA
        while (i + 16 <= pixelCount)
        {
            // Загружаем 4 overlapping блока по 12 байт каждый
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);

            // Shuffle + alpha
            var r0 = Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask);

            // Store 64 байт (16 пикселей)
            Sse2.Store(dst + (i * 4), r0);
            Sse2.Store(dst + (i * 4) + 16, r1);
            Sse2.Store(dst + (i * 4) + 32, r2);
            Sse2.Store(dst + (i * 4) + 48, r3);

            i += 16;
        }

        // 4 пикселя = 12 байт BGR → 16 байт BGRA
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 3;
            var dstOffset = i * 4;
            dst[dstOffset] = src[srcOffset];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset + 2];
            dst[dstOffset + 3] = 255;
            i++;
        }
    }

    /// <summary>
    /// AVX2: BGRA32 → BGR24 (удалить A).
    /// Для 24-bit форматов AVX2 VPSHUFB работает только in-lane, поэтому используем
    /// идентичный SSE41 код для паритета производительности.
    /// 16 пикселей за итерацию (64 байт BGRA → 48 байт BGR).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToBgr24Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask;

        // 16 пикселей = 64 байта вход → 48 байт выход
        // Используем overlapping 16-byte stores для минимизации операций
        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 4));
            var v1 = Sse2.LoadVector128(src + (i * 4) + 16);
            var v2 = Sse2.LoadVector128(src + (i * 4) + 32);
            var v3 = Sse2.LoadVector128(src + (i * 4) + 48);

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            // Overlapping stores: каждый store пишет 16 байт, но нужно только 12
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        // 4 пикселя
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);

            // 12 байт через две записи
            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));

            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset];         // B
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset + 2]; // R
            i++;
        }
    }

    #endregion

    #region AVX512 Implementations (Bgr24)

    /// <summary>AVX512BW: BGR24 → BGRA32 (добавить A=255). 16 пикселей за итерацию.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToBgra32Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);

            var r0 = Sse2.Or(Ssse3.Shuffle(v0, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);

            var lo256 = Vector256.Create(r0, r1);
            var hi256 = Vector256.Create(r2, r3);
            Avx512BW.Store(dst + (i * 4), Vector512.Create(lo256, hi256));
            i += 16;
        }

        while (i + 8 <= pixelCount)
        {
            var lo = Sse2.LoadVector128(src + (i * 3));
            var hi = Sse2.LoadVector128(src + (i * 3) + 12);
            var loResult = Sse2.Or(Ssse3.Shuffle(lo, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var hiResult = Sse2.Or(Ssse3.Shuffle(hi, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            Avx.Store(dst + (i * 4), Vector256.Create(loResult, hiResult));
            i += 8;
        }

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, Bgra32Sse41Vectors.Bgr24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        while (i < pixelCount)
        {
            var srcOffset = i * 3;
            var dstOffset = i * 4;
            dst[dstOffset] = src[srcOffset];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset + 2];
            dst[dstOffset + 3] = 255;
            i++;
        }
    }

    /// <summary>AVX512BW: BGRA32 → BGR24 (удалить A). 16 пикселей за итерацию.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToBgr24Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        while (i + 16 <= pixelCount)
        {
            var v = Avx512BW.LoadVector512(src + (i * 4));
            var v0 = v.GetLower().GetLower();
            var v1 = v.GetLower().GetUpper();
            var v2 = v.GetUpper().GetLower();
            var v3 = v.GetUpper().GetUpper();

            var r0 = Ssse3.Shuffle(v0, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            var r1 = Ssse3.Shuffle(v1, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            var r2 = Ssse3.Shuffle(v2, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            var r3 = Ssse3.Shuffle(v3, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);

            Unsafe.WriteUnaligned(dst + (i * 3), r0.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r0.AsUInt32().GetElement(2));
            Unsafe.WriteUnaligned(dst + (i * 3) + 12, r1.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 20, r1.AsUInt32().GetElement(2));
            Unsafe.WriteUnaligned(dst + (i * 3) + 24, r2.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 32, r2.AsUInt32().GetElement(2));
            Unsafe.WriteUnaligned(dst + (i * 3) + 36, r3.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 44, r3.AsUInt32().GetElement(2));
            i += 16;
        }

        while (i + 8 <= pixelCount)
        {
            var v = Avx.LoadVector256(src + (i * 4));
            var lo = v.GetLower();
            var hi = v.GetUpper();
            var loResult = Ssse3.Shuffle(lo, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            var hiResult = Ssse3.Shuffle(hi, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            Unsafe.WriteUnaligned(dst + (i * 3), loResult.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, loResult.AsUInt32().GetElement(2));
            Unsafe.WriteUnaligned(dst + (i * 3) + 12, hiResult.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 20, hiResult.AsUInt32().GetElement(2));
            i += 8;
        }

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, Bgra32Sse41Vectors.Bgra32ToBgr24ShuffleMask);
            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));
            i += 4;
        }

        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset + 2];
            i++;
        }
    }

    #endregion

    #region Scalar Implementations (Bgr24)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToBgra32Scalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (24 байт Bgr24 → 32 байт Bgra32)
        while (pixelCount >= 8)
        {
            // Пиксель 0
            dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = 255;
            // Пиксель 1
            dst[4] = src[3]; dst[5] = src[4]; dst[6] = src[5]; dst[7] = 255;
            // Пиксель 2
            dst[8] = src[6]; dst[9] = src[7]; dst[10] = src[8]; dst[11] = 255;
            // Пиксель 3
            dst[12] = src[9]; dst[13] = src[10]; dst[14] = src[11]; dst[15] = 255;
            // Пиксель 4
            dst[16] = src[12]; dst[17] = src[13]; dst[18] = src[14]; dst[19] = 255;
            // Пиксель 5
            dst[20] = src[15]; dst[21] = src[16]; dst[22] = src[17]; dst[23] = 255;
            // Пиксель 6
            dst[24] = src[18]; dst[25] = src[19]; dst[26] = src[20]; dst[27] = 255;
            // Пиксель 7
            dst[28] = src[21]; dst[29] = src[22]; dst[30] = src[23]; dst[31] = 255;

            src += 24;
            dst += 32;
            pixelCount -= 8;
        }

        // Остаток по 1 пикселю
        while (pixelCount > 0)
        {
            dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = 255;
            src += 3;
            dst += 4;
            pixelCount--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToBgr24Scalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (32 байт Bgra32 → 24 байт Bgr24)
        while (pixelCount >= 8)
        {
            // Пиксель 0
            dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2];
            // Пиксель 1
            dst[3] = src[4]; dst[4] = src[5]; dst[5] = src[6];
            // Пиксель 2
            dst[6] = src[8]; dst[7] = src[9]; dst[8] = src[10];
            // Пиксель 3
            dst[9] = src[12]; dst[10] = src[13]; dst[11] = src[14];
            // Пиксель 4
            dst[12] = src[16]; dst[13] = src[17]; dst[14] = src[18];
            // Пиксель 5
            dst[15] = src[20]; dst[16] = src[21]; dst[17] = src[22];
            // Пиксель 6
            dst[18] = src[24]; dst[19] = src[25]; dst[20] = src[26];
            // Пиксель 7
            dst[21] = src[28]; dst[22] = src[29]; dst[23] = src[30];

            src += 32;
            dst += 24;
            pixelCount -= 8;
        }

        // Остаток по 1 пикселю
        while (pixelCount > 0)
        {
            dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2];
            src += 4;
            dst += 3;
            pixelCount--;
        }
    }

    #endregion
}
