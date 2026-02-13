#pragma warning disable CA1000, CA2208, MA0051, S4136, S4144, IDE0004

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Конвертация Bgr24 ↔ Rgba32.
/// Прямая SIMD-реализация: swap B и R + добавление/удаление альфа-канала.
/// </summary>
public readonly partial struct Bgr24
{
    #region Single Pixel Conversion (Rgba32)

    /// <summary>Конвертирует Rgba32 в Bgr24 (swap R и B, отбрасывает A).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bgr24 FromRgba32(Rgba32 rgba) => new(rgba.B, rgba.G, rgba.R);

    /// <summary>Конвертирует Bgr24 в Rgba32 (swap B и R, A = 255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 ToRgba32() => new(R, G, B, 255);

    #endregion

    #region Batch Conversion (Bgr24 ↔ Rgba32)

    /// <summary>
    /// Реализованные ускорители для конвертации Bgr24 ↔ Rgba32.
    /// </summary>
    private const HardwareAcceleration Rgba32Implemented =
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2 |
        HardwareAcceleration.Avx512BW;

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgr24 с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Bgr24> destination) =>
        FromRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Rgba32 → Bgr24 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Rgba32.</param>
    /// <param name="destination">Целевой буфер Bgr24.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void FromRgba32(ReadOnlySpan<Rgba32> source, Span<Bgr24> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов:
        // SIMD использует overlapping reads (16 байт для 12 байт данных),
        // что нарушает границы чанков при многопоточной обработке
        FromRgba32Core(source, destination, selected);
    }

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgba32 с SIMD.
    /// Автоматически использует параллельную обработку для буферов >= 1024 пикселей.
    /// </summary>
    public static void ToRgba32(ReadOnlySpan<Bgr24> source, Span<Rgba32> destination) =>
        ToRgba32(source, destination, HardwareAcceleration.Auto);

    /// <summary>
    /// Пакетная конвертация Bgr24 → Rgba32 с явным указанием ускорителя.
    /// </summary>
    /// <param name="source">Исходный буфер Bgr24.</param>
    /// <param name="destination">Целевой буфер Rgba32.</param>
    /// <param name="acceleration">Разрешённые ускорители (Auto = выбор лучшего).</param>
    public static void ToRgba32(ReadOnlySpan<Bgr24> source, Span<Rgba32> destination, HardwareAcceleration acceleration)
    {
        if (source.IsEmpty) return;

        // Выбираем лучший доступный ускоритель
        var selected = ColorSpaceHelper.SelectAccelerator(acceleration, Rgba32Implemented, source.Length);

        // Параллельная обработка отключена для 24-bit форматов:
        // SIMD использует overlapping reads (16 байт для 12 байт данных),
        // что нарушает границы чанков при многопоточной обработке
        ToRgba32Core(source, destination, selected);
    }

    #endregion

    #region Core SIMD

    /// <summary>Однопоточная конвертация Rgba32 → Bgr24 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FromRgba32Core(ReadOnlySpan<Rgba32> source, Span<Bgr24> destination, HardwareAcceleration selected)
    {
        fixed (Rgba32* srcPtr = source)
        fixed (Bgr24* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Rgba32ToBgr24Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    Rgba32ToBgr24Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Rgba32ToBgr24Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Rgba32ToBgr24Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    /// <summary>Однопоточная конвертация Bgr24 → Rgba32 с выбранным ускорителем.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToRgba32Core(ReadOnlySpan<Bgr24> source, Span<Rgba32> destination, HardwareAcceleration selected)
    {
        fixed (Bgr24* srcPtr = source)
        fixed (Rgba32* dstPtr = destination)
        {
            switch (selected)
            {
                case HardwareAcceleration.Avx512BW when source.Length >= 16:
                    Bgr24ToRgba32Avx512((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Avx2 when source.Length >= 8:
                    Bgr24ToRgba32Avx2((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                case HardwareAcceleration.Sse41 when source.Length >= 4:
                    Bgr24ToRgba32Ssse3((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
                default:
                    Bgr24ToRgba32Scalar((byte*)srcPtr, (byte*)dstPtr, source.Length);
                    break;
            }
        }
    }

    #endregion

    #region AVX512 Implementations

    /// <summary>
    /// AVX512BW: RGBA32 → BGR24 (16 пикселей за итерацию).
    /// Удаляем альфа-канал и swap R ↔ B.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgba32ToBgr24Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        var shuffleMask = Bgr24Sse41Vectors.Rgba32ToBgr24ShuffleMask;

        // 16 пикселей RGBA = 64 байта → 48 байт BGR
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

            // Overlapping 16-byte stores
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);
            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));
            i += 4;
        }

        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[i * 3] = src[offset + 2];
            dst[(i * 3) + 1] = src[offset + 1];
            dst[(i * 3) + 2] = src[offset];
            i++;
        }
    }

    /// <summary>
    /// AVX512BW: BGR24 → RGBA32 (16 пикселей за итерацию).
    /// Добавляем альфа-канал и swap B ↔ R.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToRgba32Avx512(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        var shuffleMask = Bgr24Sse41Vectors.Bgr24ToRgba32ShuffleMask;
        var alphaMask = Bgr24Sse41Vectors.Alpha255Mask;

        // 16 пикселей BGR = 48 байт → 64 байта RGBA
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

            var lo256 = Vector256.Create(r0, r1);
            var hi256 = Vector256.Create(r2, r3);
            Avx512BW.Store(dst + (i * 4), Vector512.Create(lo256, hi256));
            i += 16;
        }

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

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

    #endregion

    #region AVX2 Implementations

    /// <summary>
    /// AVX2: RGBA32 → BGR24 (16 пикселей за итерацию).
    /// Использует 2x AVX2 256-bit loads + AVX2 VPSHUFB + SSE overlapping stores.
    /// 64 байт RGBA → 48 байт BGR.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgba32ToBgr24Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        // Кешируем маски в регистрах
        var shuffleMask256 = Bgr24Avx2Vectors.PackBgr24;
        var shuffleMask128 = Bgr24Sse41Vectors.Rgba32ToBgr24ShuffleMask;

        // === 16 пикселей за итерацию (64 байт RGBA → 48 байт BGR) ===
        // 2x AVX2 loads → 2x AVX2 VPSHUFB → 4x SSE overlapping stores
        while (i + 16 <= pixelCount)
        {
            // AVX2 256-bit loads (8 пикселей = 32 байта каждый)
            var rgba01 = Avx.LoadVector256(src + (i * 4));       // пиксели 0-7
            var rgba23 = Avx.LoadVector256(src + (i * 4) + 32);  // пиксели 8-15

            // AVX2 VPSHUFB: RGBA → BGR (in-lane, 12 байт данных + 4 нуля на lane)
            var bgr01 = Avx2.Shuffle(rgba01, shuffleMask256);
            var bgr23 = Avx2.Shuffle(rgba23, shuffleMask256);

            // SSE overlapping stores (каждый lane = 12 байт данных + 4 мусора)
            // bgr01.Lower = пиксели 0-3, bgr01.Upper = пиксели 4-7
            // bgr23.Lower = пиксели 8-11, bgr23.Upper = пиксели 12-15
            Sse2.Store(dst + (i * 3), bgr01.GetLower());           // байты 0-15 (используем 0-11)
            Sse2.Store(dst + (i * 3) + 12, bgr01.GetUpper());      // байты 12-27 (используем 12-23)
            Sse2.Store(dst + (i * 3) + 24, bgr23.GetLower());      // байты 24-39 (используем 24-35)
            Sse2.Store(dst + (i * 3) + 36, bgr23.GetUpper());      // байты 36-51 (используем 36-47)

            i += 16;
        }

        // 4 пикселя (SSE fallback)
        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask128);

            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));

            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[i * 3] = src[offset + 2];       // B
            dst[(i * 3) + 1] = src[offset + 1]; // G
            dst[(i * 3) + 2] = src[offset];     // R
            i++;
        }
    }

    /// <summary>
    /// AVX2: BGR24 → RGBA32 (16 пикселей за итерацию).
    /// Использует 4x SSE loads + 2x AVX2 shuffles + 2x AVX2 stores.
    /// 48 байт BGR → 64 байт RGBA.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToRgba32Avx2(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        // Кешируем маски в регистрах
        var shuffleMask128 = Bgr24Sse41Vectors.Bgr24ToRgba32ShuffleMask;
        var alphaMask128 = Bgr24Sse41Vectors.Alpha255Mask;

        // === 32 пикселя за итерацию (96 байт BGR → 128 байт RGBA) ===
        // 8x SSE overlapping loads → 8x SSSE3 shuffle → 8x SSE stores
        // AVX2 Vector256.Create() генерирует vinsertf128 (~3 cycles overhead),
        // поэтому используем чистый SSE с максимальным unroll для ILP.
        while (i + 32 <= pixelCount)
        {
            // 8x overlapping SSE loads (каждый читает 16 байт, используем 12)
            var bgr0 = Sse2.LoadVector128(src + (i * 3));
            var bgr1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var bgr2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var bgr3 = Sse2.LoadVector128(src + (i * 3) + 36);
            var bgr4 = Sse2.LoadVector128(src + (i * 3) + 48);
            var bgr5 = Sse2.LoadVector128(src + (i * 3) + 60);
            var bgr6 = Sse2.LoadVector128(src + (i * 3) + 72);
            var bgr7 = Sse2.LoadVector128(src + (i * 3) + 84);

            // 8x SSSE3 shuffle + OR alpha
            var rgba0 = Sse2.Or(Ssse3.Shuffle(bgr0, shuffleMask128), alphaMask128);
            var rgba1 = Sse2.Or(Ssse3.Shuffle(bgr1, shuffleMask128), alphaMask128);
            var rgba2 = Sse2.Or(Ssse3.Shuffle(bgr2, shuffleMask128), alphaMask128);
            var rgba3 = Sse2.Or(Ssse3.Shuffle(bgr3, shuffleMask128), alphaMask128);
            var rgba4 = Sse2.Or(Ssse3.Shuffle(bgr4, shuffleMask128), alphaMask128);
            var rgba5 = Sse2.Or(Ssse3.Shuffle(bgr5, shuffleMask128), alphaMask128);
            var rgba6 = Sse2.Or(Ssse3.Shuffle(bgr6, shuffleMask128), alphaMask128);
            var rgba7 = Sse2.Or(Ssse3.Shuffle(bgr7, shuffleMask128), alphaMask128);

            // 8x SSE stores (каждый пишет 16 байт = 4 RGBA пикселя)
            Sse2.Store(dst + (i * 4), rgba0);
            Sse2.Store(dst + (i * 4) + 16, rgba1);
            Sse2.Store(dst + (i * 4) + 32, rgba2);
            Sse2.Store(dst + (i * 4) + 48, rgba3);
            Sse2.Store(dst + (i * 4) + 64, rgba4);
            Sse2.Store(dst + (i * 4) + 80, rgba5);
            Sse2.Store(dst + (i * 4) + 96, rgba6);
            Sse2.Store(dst + (i * 4) + 112, rgba7);

            i += 32;
        }

        // 16 пикселей (4x unroll)
        while (i + 16 <= pixelCount)
        {
            var bgr0 = Sse2.LoadVector128(src + (i * 3));
            var bgr1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var bgr2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var bgr3 = Sse2.LoadVector128(src + (i * 3) + 36);

            var rgba0 = Sse2.Or(Ssse3.Shuffle(bgr0, shuffleMask128), alphaMask128);
            var rgba1 = Sse2.Or(Ssse3.Shuffle(bgr1, shuffleMask128), alphaMask128);
            var rgba2 = Sse2.Or(Ssse3.Shuffle(bgr2, shuffleMask128), alphaMask128);
            var rgba3 = Sse2.Or(Ssse3.Shuffle(bgr3, shuffleMask128), alphaMask128);

            Sse2.Store(dst + (i * 4), rgba0);
            Sse2.Store(dst + (i * 4) + 16, rgba1);
            Sse2.Store(dst + (i * 4) + 32, rgba2);
            Sse2.Store(dst + (i * 4) + 48, rgba3);

            i += 16;
        }

        // 4 пикселя (SSE fallback)
        while (i + 4 <= pixelCount)
        {
            var bgr = Sse2.LoadVector128(src + (i * 3));
            var rgba = Sse2.Or(Ssse3.Shuffle(bgr, shuffleMask128), alphaMask128);
            Sse2.Store(dst + (i * 4), rgba);
            i += 4;
        }

        // Остаток scalar
        while (i < pixelCount)
        {
            var srcOffset = i * 3;
            var dstOffset = i * 4;
            dst[dstOffset] = src[srcOffset + 2];     // R
            dst[dstOffset + 1] = src[srcOffset + 1]; // G
            dst[dstOffset + 2] = src[srcOffset];     // B
            dst[dstOffset + 3] = 255;                // A
            i++;
        }
    }

    #endregion

    #region SSSE3 Implementations

    /// <summary>SSSE3: RGBA32 → BGR24.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Rgba32ToBgr24Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        var shuffleMask = Bgr24Sse41Vectors.Rgba32ToBgr24ShuffleMask;

        // 4x unroll: 16 пикселей = 64 байта RGBA → 48 байт BGR
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

            // Overlapping 16-byte stores
            Sse2.Store(dst + (i * 3), r0);
            Sse2.Store(dst + (i * 3) + 12, r1);
            Sse2.Store(dst + (i * 3) + 24, r2);
            Sse2.Store(dst + (i * 3) + 36, r3);

            i += 16;
        }

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 4));
            var r = Ssse3.Shuffle(v, shuffleMask);

            // Записываем 12 байт
            Unsafe.WriteUnaligned(dst + (i * 3), r.AsUInt64().GetElement(0));
            Unsafe.WriteUnaligned(dst + (i * 3) + 8, r.AsUInt32().GetElement(2));

            i += 4;
        }

        while (i < pixelCount)
        {
            var offset = i * 4;
            dst[i * 3] = src[offset + 2];
            dst[(i * 3) + 1] = src[offset + 1];
            dst[(i * 3) + 2] = src[offset];
            i++;
        }
    }

    /// <summary>SSSE3: BGR24 → RGBA32.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Bgr24ToRgba32Ssse3(byte* src, byte* dst, int pixelCount)
    {
        var i = 0;

        // Shuffle: BGR → RGB + добавить слот для alpha
        // 0x80 в позиции alpha даёт 0, затем Or с alphaMask даёт 255
        var shuffleMask = Bgr24Sse41Vectors.Bgr24ToRgba32ShuffleMask;
        var alphaMask = Bgr24Sse41Vectors.Alpha255Mask;

        // 8x unroll: 32 пикселя BGR = 96 байт → 128 байт RGBA
        // Больший unroll снижает loop overhead и улучшает ILP
        while (i + 32 <= pixelCount)
        {
            var v0 = Sse2.LoadVector128(src + (i * 3));
            var v1 = Sse2.LoadVector128(src + (i * 3) + 12);
            var v2 = Sse2.LoadVector128(src + (i * 3) + 24);
            var v3 = Sse2.LoadVector128(src + (i * 3) + 36);
            var v4 = Sse2.LoadVector128(src + (i * 3) + 48);
            var v5 = Sse2.LoadVector128(src + (i * 3) + 60);
            var v6 = Sse2.LoadVector128(src + (i * 3) + 72);
            var v7 = Sse2.LoadVector128(src + (i * 3) + 84);

            var r0 = Sse2.Or(Ssse3.Shuffle(v0, shuffleMask), alphaMask);
            var r1 = Sse2.Or(Ssse3.Shuffle(v1, shuffleMask), alphaMask);
            var r2 = Sse2.Or(Ssse3.Shuffle(v2, shuffleMask), alphaMask);
            var r3 = Sse2.Or(Ssse3.Shuffle(v3, shuffleMask), alphaMask);
            var r4 = Sse2.Or(Ssse3.Shuffle(v4, shuffleMask), alphaMask);
            var r5 = Sse2.Or(Ssse3.Shuffle(v5, shuffleMask), alphaMask);
            var r6 = Sse2.Or(Ssse3.Shuffle(v6, shuffleMask), alphaMask);
            var r7 = Sse2.Or(Ssse3.Shuffle(v7, shuffleMask), alphaMask);

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

        // 4x unroll: 16 пикселей BGR = 48 байт → 64 байта RGBA
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

        while (i + 4 <= pixelCount)
        {
            var v = Sse2.LoadVector128(src + (i * 3));
            var r = Sse2.Or(Ssse3.Shuffle(v, shuffleMask), alphaMask);
            Sse2.Store(dst + (i * 4), r);
            i += 4;
        }

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

    #endregion

    #region Scalar Implementations

    /// <summary>
    /// Scalar: RGBA32 → BGR24 с оптимизированными uint операциями.
    /// Swap R↔B и упаковка 4→3 байта.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Rgba32ToBgr24Scalar(byte* src, byte* dst, int pixelCount)
    {
        var s = (uint*)src;
        var d = dst;

        var i = 0;

        // 4 пикселя за итерацию (16 байт RGBA → 12 байт BGR)
        while (i + 4 <= pixelCount)
        {
            // Читаем 4 RGBA пикселя
            var rgba0 = s[0]; // R0 G0 B0 A0
            var rgba1 = s[1]; // R1 G1 B1 A1
            var rgba2 = s[2]; // R2 G2 B2 A2
            var rgba3 = s[3]; // R3 G3 B3 A3

            // Swap R↔B: [R, G, B, A] → [B, G, R]
            // R=byte0, G=byte1, B=byte2, A=byte3
            // Нужно: B=byte0, G=byte1, R=byte2

            // Пиксель 0: записываем 3 байта по смещению 0
            // Формируем uint с BGR0 в младших 3 байтах
            var bgr0 = ((rgba0 >> 16) & 0xFF) | (rgba0 & 0xFF00) | ((rgba0 & 0xFF) << 16);

            // Пиксель 1: записываем 3 байта по смещению 3
            var bgr1 = ((rgba1 >> 16) & 0xFF) | (rgba1 & 0xFF00) | ((rgba1 & 0xFF) << 16);

            // Пиксель 2: записываем 3 байта по смещению 6
            var bgr2 = ((rgba2 >> 16) & 0xFF) | (rgba2 & 0xFF00) | ((rgba2 & 0xFF) << 16);

            // Пиксель 3: записываем 3 байта по смещению 9
            var bgr3 = ((rgba3 >> 16) & 0xFF) | (rgba3 & 0xFF00) | ((rgba3 & 0xFF) << 16);

            // Overlapping writes: записываем uint, следующий перезаписывает лишний байт
            *(uint*)(d + 0) = bgr0;  // байты 0-3 (используем 0-2)
            *(uint*)(d + 3) = bgr1;  // байты 3-6 (используем 3-5, перезаписывает байт 3)
            *(uint*)(d + 6) = bgr2;  // байты 6-9 (используем 6-8)
            *(uint*)(d + 9) = bgr3;  // байты 9-12 (используем 9-11)

            s += 4;
            d += 12;
            i += 4;
        }

        // Остаток побайтово
        var srcBytes = (byte*)s;
        while (i < pixelCount)
        {
            var idx = (i - (pixelCount - (pixelCount % 4))) * 4;
            d[0] = srcBytes[idx + 2];     // B
            d[1] = srcBytes[idx + 1];     // G
            d[2] = srcBytes[idx];         // R
            d += 3;
            i++;
        }
    }

    /// <summary>
    /// Scalar: BGR24 → RGBA32 с оптимизированными uint операциями.
    /// Swap B↔R через побитовые операции + OR alpha.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Bgr24ToRgba32Scalar(byte* src, byte* dst, int pixelCount)
    {
        var s = src;
        var d = (uint*)dst;

        var i = 0;

        // 4 пикселя за итерацию (12 байт BGR → 16 байт RGBA)
        // Каждый uint read захватывает 4 байта, используем 3
        while (i + 4 <= pixelCount)
        {
            // Читаем 4 байта, используем 3 (BGR + мусор)
            // bgr0 = [B0, G0, R0, B1] → нужно [R0, G0, B0, 255]
            var bgr0 = *(uint*)(s + 0);  // B0 G0 R0 X
            var bgr1 = *(uint*)(s + 3);  // B1 G1 R1 X
            var bgr2 = *(uint*)(s + 6);  // B2 G2 R2 X
            var bgr3 = *(uint*)(s + 9);  // B3 G3 R3 X

            // Swap B↔R: извлекаем B, G, R и пересобираем как R, G, B, 255
            // Позиции: B=byte0, G=byte1, R=byte2, X=byte3
            // Нужно: R=byte0, G=byte1, B=byte2, A=byte3
            d[0] = ((bgr0 >> 16) & 0xFF) | (bgr0 & 0xFF00) | ((bgr0 & 0xFF) << 16) | 0xFF000000;
            d[1] = ((bgr1 >> 16) & 0xFF) | (bgr1 & 0xFF00) | ((bgr1 & 0xFF) << 16) | 0xFF000000;
            d[2] = ((bgr2 >> 16) & 0xFF) | (bgr2 & 0xFF00) | ((bgr2 & 0xFF) << 16) | 0xFF000000;
            d[3] = ((bgr3 >> 16) & 0xFF) | (bgr3 & 0xFF00) | ((bgr3 & 0xFF) << 16) | 0xFF000000;

            s += 12;
            d += 4;
            i += 4;
        }

        // Остаток побайтово
        var dstBytes = (byte*)d;
        while (i < pixelCount)
        {
            var srcOffset = (i - (pixelCount - (pixelCount % 4))) * 3;
            dstBytes[0] = s[srcOffset + 2];     // R
            dstBytes[1] = s[srcOffset + 1];     // G
            dstBytes[2] = s[srcOffset];         // B
            dstBytes[3] = 255;                  // A
            dstBytes += 4;
            i++;
        }
    }

    #endregion
}
