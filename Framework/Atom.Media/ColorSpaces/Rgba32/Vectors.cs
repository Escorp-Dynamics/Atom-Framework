using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для Rgba32 (Vector128).
/// </summary>
internal static class Rgba32Sse41Vectors
{
    #region RGB24 ↔ RGBA32 Shuffle Masks

    /// <summary>
    /// RGB24 → RGBA32: добавление альфа-канала.
    /// R0G0B0 R1G1B1 R2G2B2 R3G3B3 → R0G0B0A0 R1G1B1A1 R2G2B2A2 R3G3B3A3
    /// 0x80 будет заменён на 255 через OR с Alpha255Mask.
    /// </summary>
    public static Vector128<byte> Rgb24ToRgba32ShuffleMask { get; } = Vector128.Create(
        0, 1, 2, 0x80,   // R0G0B0A0
        3, 4, 5, 0x80,         // R1G1B1A1
        6, 7, 8, 0x80,         // R2G2B2A2
        9, 10, 11, 0x80);      // R3G3B3A3

    /// <summary>
    /// RGBA32 → RGB24: удаление альфа-канала.
    /// R0G0B0A0 R1G1B1A1 R2G2B2A2 R3G3B3A3 → R0G0B0 R1G1B1 R2G2B2 R3G3B3
    /// </summary>
    public static Vector128<byte> Rgba32ToRgb24ShuffleMask { get; } = Vector128.Create(
        0, 1, 2,         // R0G0B0
        4, 5, 6,               // R1G1B1
        8, 9, 10,              // R2G2B2
        12, 13, 14,            // R3G3B3
        0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (4 пикселя).</summary>
    public static Vector128<byte> Alpha255Mask { get; } = Vector128.Create(
        0, 0, 0, 255,
        0, 0, 0, 255,
        0, 0, 0, 255,
        0, 0, 0, 255);

    #endregion

    #region YCbCr Deinterleave/Interleave Masks

    /// <summary>
    /// Извлечение R из RGBA32 (4 пикселя → 4 байта в позициях 0-3).
    /// RGBA layout: R0G0B0A0 R1G1B1A1 R2G2B2A2 R3G3B3A3
    /// </summary>
    public static Vector128<byte> Rgba32ShuffleR { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGBA32.</summary>
    public static Vector128<byte> Rgba32ShuffleG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGBA32.</summary>
    public static Vector128<byte> Rgba32ShuffleB { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Broadcast 255 для альфа-канала (YCbCr → RGBA).</summary>
    public static Vector128<byte> YCbCrAlpha255Mask { get; } = Vector128.Create((byte)255);

    /// <summary>YCb → RGBA interleave (первые 16 байт).</summary>
    public static Vector128<byte> YCbToRgbaShuffleMask0 { get; } = Vector128.Create(
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 8, 9, 0x80, 10);

    /// <summary>Cr → RGBA interleave (первые 16 байт).</summary>
    public static Vector128<byte> CrToRgbaShuffleMask0 { get; } = Vector128.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80);

    /// <summary>YCb → RGBA interleave (оставшиеся 8 байт).</summary>
    public static Vector128<byte> YCbToRgbaShuffleMask1 { get; } = Vector128.Create(
        11, 0x80, 12, 13, 0x80, 14, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Cr → RGBA interleave (оставшиеся 8 байт).</summary>
    public static Vector128<byte> CrToRgbaShuffleMask1 { get; } = Vector128.Create(
        0x80, 5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Y (первые 6 байт из 16).</summary>
    public static Vector128<byte> RgbaDeinterleaveY0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Y (байты 6-7 из второго блока).</summary>
    public static Vector128<byte> RgbaDeinterleaveY1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Cb (первые 5 байт).</summary>
    public static Vector128<byte> RgbaDeinterleaveCb0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Cb (байты 5-7 из второго блока).</summary>
    public static Vector128<byte> RgbaDeinterleaveCb1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Cr (первые 5 байт).</summary>
    public static Vector128<byte> RgbaDeinterleaveCr0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA deinterleave Cr (байты 5-7 из второго блока).</summary>
    public static Vector128<byte> RgbaDeinterleaveCr1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для Rgba32 (Vector256).
/// </summary>
internal static class Rgba32Avx2Vectors
{
    #region RGB24 ↔ RGBA32 Shuffle Masks

    /// <summary>
    /// RGBA32 → RGB24 AVX2 (работает в каждой 128-bit lane).
    /// Каждая lane: 4 RGBA пикселя → 12 байт RGB + 4 нуля.
    /// </summary>
    public static Vector256<byte> Rgba32ToRgb24ShuffleMask { get; } = Vector256.Create(
        0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80,
        0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// RGB24 → RGBA32 AVX2 (работает в каждой 128-bit lane).
    /// Каждая lane берёт 12 байт RGB и расширяет до 16 байт RGBA.
    /// </summary>
    public static Vector256<byte> Rgb24ToRgba32ShuffleMask { get; } = Vector256.Create(
        0, 1, 2, 0x80,   // R0G0B0A0
        3, 4, 5, 0x80,         // R1G1B1A1
        6, 7, 8, 0x80,         // R2G2B2A2
        9, 10, 11, 0x80,       // R3G3B3A3
        0, 1, 2, 0x80,         // R4G4B4A4 (lane 1)
        3, 4, 5, 0x80,         // R5G5B5A5
        6, 7, 8, 0x80,         // R6G6B6A6
        9, 10, 11, 0x80);      // R7G7B7A7

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (8 пикселей).</summary>
    public static Vector256<byte> Alpha255Mask { get; } = Vector256.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion
}
