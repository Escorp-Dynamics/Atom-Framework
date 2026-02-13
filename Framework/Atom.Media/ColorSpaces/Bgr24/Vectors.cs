using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для Bgr24 (Vector128).
/// </summary>
internal static class Bgr24Sse41Vectors
{
    #region BGR ↔ RGB Swap Masks

    /// <summary>
    /// Shuffle маска для BGR ↔ RGB: swap первого и третьего байта в каждом триплете.
    /// Обрабатывает 5 пикселей (15 байт) за раз.
    /// [2,1,0, 5,4,3, 8,7,6, 11,10,9, 14,13,12, 0x80]
    /// </summary>
    public static Vector128<byte> SwapRB24ShuffleMask { get; } = Vector128.Create(
        2, 1, 0,         // пиксель 0
        5, 4, 3,               // пиксель 1
        8, 7, 6,               // пиксель 2
        11, 10, 9,             // пиксель 3
        14, 13, 12,            // пиксель 4
        0x80);                 // padding

    #endregion

    #region BGR24 ↔ RGBA32 Shuffle Masks

    /// <summary>
    /// RGBA32 → BGR24: swap R↔B + удаление альфа.
    /// RGBA: R0G0B0A0 R1G1B1A1 R2G2B2A2 R3G3B3A3
    /// BGR:  B0G0R0 B1G1R1 B2G2R2 B3G3R3 (12 байт из 16)
    /// </summary>
    public static Vector128<byte> Rgba32ToBgr24ShuffleMask { get; } = Vector128.Create(
        2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// BGR24 → RGBA32: swap B↔R + добавление альфа.
    /// BGR:  B0G0R0 B1G1R1 B2G2R2 B3G3R3 (12 байт)
    /// RGBA: R0G0B0A0 R1G1B1A1 R2G2B2A2 R3G3B3A3 (16 байт)
    /// </summary>
    public static Vector128<byte> Bgr24ToRgba32ShuffleMask { get; } = Vector128.Create(
        2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80);

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (4 пикселя).</summary>
    public static Vector128<byte> Alpha255Mask { get; } = Vector128.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для Bgr24 (Vector256).
/// </summary>
internal static class Bgr24Avx2Vectors
{
    #region BGR ↔ RGB Swap Masks

    /// <summary>
    /// AVX2 Shuffle маска для BGR ↔ RGB (каждая 128-bit lane).
    /// Обрабатывает 5 пикселей (15 байт) в каждой lane = 10 пикселей за Vector256.
    /// </summary>
    public static Vector256<byte> SwapRB24ShuffleMask { get; } = Vector256.Create(
        // Lane 0: пиксели 0-4
        2, 1, 0, 5, 4, 3, 8, 7, 6, 11, 10, 9, 14, 13, 12, 0x80,
        // Lane 1: пиксели 5-9
        2, 1, 0, 5, 4, 3, 8, 7, 6, 11, 10, 9, 14, 13, 12, 0x80);

    #endregion

    #region BGR24 ↔ RGBA32 AVX2 Shuffle Masks

    /// <summary>
    /// AVX2: RGBA32 → BGR24 (работает в каждой 128-bit lane).
    /// Каждая lane: 4 RGBA пикселя (16 байт) → 12 байт BGR + 4 нуля.
    /// </summary>
    public static Vector256<byte> PackBgr24 { get; } = Vector256.Create(
        // Lane 0
        2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0x80, 0x80, 0x80, 0x80,
        // Lane 1
        2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// VPERMD индексы для упаковки 24 байт из 2 lanes в непрерывный блок.
    /// После VPSHUFB: [12 байт | 4 нуля | 12 байт | 4 нуля]
    /// </summary>
    public static Vector256<int> PermuteBgr24 { get; } = Vector256.Create(0, 1, 2, 4, 5, 6, 3, 7);

    /// <summary>
    /// VPERMD индексы для распределения 24 байт BGR по 2 lanes.
    /// </summary>
    public static Vector256<int> PermuteBgr24ToLanes { get; } = Vector256.Create(0, 1, 2, 6, 3, 4, 5, 7);

    /// <summary>
    /// AVX2: BGR24 → RGBA32 (работает в каждой 128-bit lane).
    /// Каждая lane: 12 байт BGR → 16 байт RGBA.
    /// </summary>
    public static Vector256<byte> UnpackRgba32 { get; } = Vector256.Create(
        // Lane 0
        2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80,
        // Lane 1
        2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80);

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (8 пикселей).</summary>
    public static Vector256<byte> Alpha255Mask { get; } = Vector256.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion
}
