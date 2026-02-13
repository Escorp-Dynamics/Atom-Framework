#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144, IDE0004

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgra32 ↔ Rgb24.
/// SIMD: swap B и R + добавление/удаление альфа-канала.
/// </summary>
public readonly partial struct Bgra32
{
    /// <summary>
    /// Реализованные ускорители для конвертации Bgra32 ↔ Rgb24.
    /// </summary>
    private const HardwareAcceleration Rgb24Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    #region Single Pixel Conversion (Rgb24)

    /// <summary>Конвертирует Rgb24 в Bgra32 (swap R и B, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgra32 FromRgb24(Rgb24 rgb) => new(rgb.B, rgb.G, rgb.R, 255);

    /// <summary>Конвертирует Bgra32 в Rgb24 (swap B и R, отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgb24 ToRgb24() => new(R, G, B);

    #endregion

    #region Batch Conversion (Bgra32 ↔ Rgb24)

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgra32 с SIMD.
    /// </summary>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Bgra32> destination) =>
        FromRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgb24 → Bgra32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Rgb24.</param>
    /// <param name="destination">Целевой буфер Bgra32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void FromRgb24(ReadOnlySpan<Rgb24> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов: SIMD использует overlapping reads
        FromRgb24Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgb24 с SIMD.
    /// </summary>
    public static void ToRgb24(ReadOnlySpan<Bgra32> source, Span<Rgb24> destination) =>
        ToRgb24(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgra32 → Rgb24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgra32.</param>
    /// <param name="destination">Целевой буфер Rgb24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void ToRgb24(ReadOnlySpan<Bgra32> source, Span<Rgb24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgb24Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов: SIMD использует overlapping reads
        ToRgb24Core(source, destination, selected);
    }

    #endregion

    #region Core SIMD (Rgb24)

    /// <summary>Однопоточная SIMD конвертация Rgb24 → Bgra32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgb24Core(ReadOnlySpan<Rgb24> source, Span<Bgra32> destination, HardwareAcceleration selected)
    {
        fixed (Rgb24* srcPtr = source)
        fixed (Bgra32* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Rgb24ToBgra32Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    Rgb24ToBgra32Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Rgb24ToBgra32Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Rgb24ToBgra32Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Однопоточная SIMD конвертация Bgra32 → Rgb24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgb24Core(ReadOnlySpan<Bgra32> source, Span<Rgb24> destination, HardwareAcceleration selected)
    {
        fixed (Bgra32* srcPtr = source)
        fixed (Rgb24* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Bgra32ToRgb24Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 16:
                    Bgra32ToRgb24Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Bgra32ToRgb24Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Bgra32ToRgb24Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    #endregion



    #region SSSE3 Implementations (Rgb24)

    /// <summary>
    /// SSSE3: RGB24 → BGRA32 (swap R↔B + добавить A=255).
    /// 16 пикселей за итерацию с 4x unroll.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgb24ToBgra32Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask;
        var alphaMask = Bgra32Sse41Vectors.Alpha255Mask;

        // 16 пикселей: 48 байт вход → 64 байта выход
        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);

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
            dst[dstOffset] = src[srcOffset + 2];     // B
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset];     // R
            dst[dstOffset + 3] = 255;                // A
            i++;
        }
    }

    /// <summary>
    /// SSSE3: BGRA32 → RGB24 (swap B↔R + удалить A).
    /// 16 пикселей за итерацию с 4x unroll и overlapping stores.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToRgb24Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgra32ToRgb24ShuffleMask;

        // 16 пикселей: 64 байта вход → 48 байт выход
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

            // Overlapping stores: 16-byte writes for 12-byte data
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        // 4 пикселя — overlapping SIMD stores (нужен запас 4 байта)
        while (i + 6 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);

            // 12 байт через overlapping 8-byte stores: [0-7] и [4-11]
            Sse2.StoreLow((double*)(dst + (i * 3)), r.AsDouble());
            Sse2.StoreLow((double*)(dst + (i * 3) + 4), Sse2.ShiftRightLogical128BitLane(r, 4).AsDouble());

            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset + 2];     // R
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset];     // B
            i++;
        }
    }

    #endregion

    #region AVX2 Implementations (Rgb24)

    /// <summary>
    /// AVX2: RGB24 → BGRA32 (swap R↔B + добавить A=255).
    /// Использует SSE shuffle с минимальным overhead для максимальной производительности.
    /// 32 пикселя за итерацию (96 байт RGB → 128 байт BGRA).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgb24ToBgra32Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask;
        var alphaMask = Bgra32Sse41Vectors.Alpha255Mask;

        // 32 пикселя RGB = 96 байт → 128 байт BGRA
        while (i + 32 <= pixelCount)
        {
            // Загружаем 8 overlapping блоков по 12 байт каждый
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);
            var v4 = Sse2.LoadVector128(src + (i * 3) + 48);
            var v5 = Sse2.LoadVector128(src + (i * 3) + 60);
            var v6 = Sse2.LoadVector128(src + (i * 3) + 72);
            var v7 = Sse2.LoadVector128(src + (i * 3) + 84);

            // Shuffle RGB→BGR + добавить A=255
            var r0 = Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask);
            var r4 = Sse2.Or(Ssse3.Shuffle(v4, shuffleMask), alphaMask);
            var r5 = Sse2.Or(Ssse3.Shuffle(v5, shuffleMask), alphaMask);
            var r6 = Sse2.Or(Ssse3.Shuffle(v6, shuffleMask), alphaMask);
            var r7 = Sse2.Or(Ssse3.Shuffle(v7, shuffleMask), alphaMask);

            // Store 128 байт (32 пикселя)
            Sse2.Store(dst + (i * 4), r0);
            Sse2.Store(dst + (i * 4) + 16, r1);
            Sse2.Store(dst + (i * 4) + 32, r2);
            Sse2.Store(dst + (i * 4) + 48, r3);
            Sse2.Store(dst + (i * 4) + 64, r4);
            Sse2.Store(dst + (i * 4) + 80, r5);
            Sse2.Store(dst + (i * 4) + 96, r6);
            Sse2.Store(dst + (i * 4) + 112, r7);

            i += 32;
        }

        // 16 пикселей (fallback)
        while (i + 16 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);

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

        // 4 пикселя = 12 байт RGB → 16 байт BGRA
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
            dst[dstOffset] = src[srcOffset + 2];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset];
            dst[dstOffset + 3] = 255;
            i++;
        }
    }

    /// <summary>
    /// AVX2: BGRA32 → RGB24 (swap B↔R + удалить A).
    /// Для 24-bit форматов AVX2 VPSHUFB работает только in-lane,
    /// используем SSE код для паритета с SSE41.
    /// 16 пикселей за итерацию (64 байт BGRA → 48 байт RGB).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToRgb24Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgra32ToRgb24ShuffleMask;

        // 16 пикселей: 64 байта вход → 48 байт выход
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

            // Overlapping stores: 16-byte writes for 12-byte data
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        // 4 пикселя — overlapping SIMD stores (нужен запас 4 байта)
        while (i + 6 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);

            // 12 байт через overlapping 8-byte stores: [0-7] и [4-11]
            Sse2.StoreLow((double*)(dst + (i * 3)), r.AsDouble());
            Sse2.StoreLow((double*)(dst + (i * 3) + 4), Sse2.ShiftRightLogical128BitLane(r, 4).AsDouble());

            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset + 2];     // R
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset];     // B
            i++;
        }
    }

    #endregion

    #region AVX512 Implementations (Rgb24)

    /// <summary>AVX512BW: RGB24 → BGRA32 (swap R↔B + добавить A=255). 16 пикселей за итерацию.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgb24ToBgra32Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        // 16 пикселей RGB24 = 48 байт → 64 байта BGRA32
        while (i + 16 <= pixelCount)
        {
            // Обрабатываем по 4 пикселя в каждой 128-битной секции
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);

            var r0 = Sse2.Or(Ssse3.Shuffle(v0, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);

            var lo256 = Vector256.Create(r0, r1);
            var hi256 = Vector256.Create(r2, r3);
            var result = Vector512.Create(lo256, hi256);
            Avx512BW.Store(dst + (i * 4), result);

            i += 16;
        }

        // Остаток AVX2
        while (i + 8 <= pixelCount)
        {
            var lo = Sse2.LoadVector128(src + (i * 3));
            var hi = Sse2.LoadVector128(src + (i * 3) + 12);

            var loResult = Sse2.Or(Ssse3.Shuffle(lo, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            var hiResult = Sse2.Or(Ssse3.Shuffle(hi, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);

            Avx.Store(dst + (i * 4), Vector256.Create(loResult, hiResult));
            i += 8;
        }

        // Остаток SSE
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, Bgra32Sse41Vectors.Rgb24ToBgra32ShuffleMask), Bgra32Sse41Vectors.Alpha255Mask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 3;
            var dstOffset = i * 4;
            dst[dstOffset] = src[srcOffset + 2];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset];
            dst[dstOffset + 3] = 255;
            i++;
        }
    }

    /// <summary>AVX512BW: BGRA32 → RGB24 (swap B↔R + удалить A). 16 пикселей за итерацию.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToRgb24Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;
        var shuffleMask = Bgra32Sse41Vectors.Bgra32ToRgb24ShuffleMask;

        // 16 пикселей BGRA32 = 64 байта → 48 байт RGB24
        // Последний store пишет байты 36-51, нужен запас 4 байта = минимум 18 пикселей
        while (i + 18 <= pixelCount)
        {
            var v = Avx512BW.LoadVector512(src + (i * 4));

            // Обрабатываем по 4 пикселя в каждой 128-битной секции
            var v0 = v.GetLower().GetLower();
            var v1 = v.GetLower().GetUpper();
            var v2 = v.GetUpper().GetLower();
            var v3 = v.GetUpper().GetUpper();

            var r0 = Ssse3.Shuffle(v0, shuffleMask);
            var r1 = Ssse3.Shuffle(v1, shuffleMask);
            var r2 = Ssse3.Shuffle(v2, shuffleMask);
            var r3 = Ssse3.Shuffle(v3, shuffleMask);

            // Записываем 48 байт через overlapping SIMD stores (каждый 16 байт, шаг 12)
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        // Остаток AVX2 (8 пикселей), нужно 10 для запаса
        while (i + 10 <= pixelCount)
        {
            var v = Avx.LoadVector256(src + (i * 4));
            var lo = v.GetLower();
            var hi = v.GetUpper();

            var loResult = Ssse3.Shuffle(lo, shuffleMask);
            var hiResult = Ssse3.Shuffle(hi, shuffleMask);

            // Overlapping stores: 16-byte writes for 12-byte data
            Sse2.Store(dst + (i * 3), loResult);
            Sse2.Store(dst + (i * 3) + 12, hiResult);
            i += 8;
        }

        // Остаток SSE (4 пикселя), нужно 6 для запаса
        while (i + 6 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);
            Sse2.StoreLow((double*)(dst + (i * 3)), r.AsDouble());
            Sse2.StoreLow((double*)(dst + (i * 3) + 4), Sse2.ShiftRightLogical128BitLane(r, 4).AsDouble());
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 4;
            var dstOffset = i * 3;
            dst[dstOffset] = src[srcOffset + 2];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset];
            i++;
        }
    }

    #endregion

    #region Scalar Implementations (Rgb24)

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgb24ToBgra32Scalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (24 байт Rgb24 → 32 байт Bgra32)
        // RGB→BGRA: R↔B swap + добавить A=255
        while (pixelCount >= 8)
        {
            // Пиксель 0: RGB → BGRA (swap R↔B)
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; dst[3] = 255;
            // Пиксель 1
            dst[4] = src[5]; dst[5] = src[4]; dst[6] = src[3]; dst[7] = 255;
            // Пиксель 2
            dst[8] = src[8]; dst[9] = src[7]; dst[10] = src[6]; dst[11] = 255;
            // Пиксель 3
            dst[12] = src[11]; dst[13] = src[10]; dst[14] = src[9]; dst[15] = 255;
            // Пиксель 4
            dst[16] = src[14]; dst[17] = src[13]; dst[18] = src[12]; dst[19] = 255;
            // Пиксель 5
            dst[20] = src[17]; dst[21] = src[16]; dst[22] = src[15]; dst[23] = 255;
            // Пиксель 6
            dst[24] = src[20]; dst[25] = src[19]; dst[26] = src[18]; dst[27] = 255;
            // Пиксель 7
            dst[28] = src[23]; dst[29] = src[22]; dst[30] = src[21]; dst[31] = 255;

            src += 24;
            dst += 32;
            pixelCount -= 8;
        }

        // Остаток по 1 пикселю
        while (pixelCount > 0)
        {
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; dst[3] = 255;
            src += 3;
            dst += 4;
            pixelCount--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgra32ToRgb24Scalar(byte* src, byte* dst, int pixelCount)
    {
        // 8 пикселей за итерацию (32 байт Bgra32 → 24 байт Rgb24)
        // BGRA→RGB: B↔R swap + отбросить A
        while (pixelCount >= 8)
        {
            // Пиксель 0: BGRA → RGB (swap B↔R)
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0];
            // Пиксель 1
            dst[3] = src[6]; dst[4] = src[5]; dst[5] = src[4];
            // Пиксель 2
            dst[6] = src[10]; dst[7] = src[9]; dst[8] = src[8];
            // Пиксель 3
            dst[9] = src[14]; dst[10] = src[13]; dst[11] = src[12];
            // Пиксель 4
            dst[12] = src[18]; dst[13] = src[17]; dst[14] = src[16];
            // Пиксель 5
            dst[15] = src[22]; dst[16] = src[21]; dst[17] = src[20];
            // Пиксель 6
            dst[18] = src[26]; dst[19] = src[25]; dst[20] = src[24];
            // Пиксель 7
            dst[21] = src[30]; dst[22] = src[29]; dst[23] = src[28];

            src += 32;
            dst += 24;
            pixelCount -= 8;
        }

        // Остаток по 1 пикселю
        while (pixelCount > 0)
        {
            dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0];
            src += 4;
            dst += 3;
            pixelCount--;
        }
    }

    #endregion
}
