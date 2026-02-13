using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// Скалярные константы для Gray8 ↔ RGB конвертации.
/// ITU-R BT.601 коэффициенты.
/// </summary>
internal static class Gray8Scalars
{
    #region ITU-R BT.601 Q15 Coefficients (×32768)

    // 0.299 × 32768 = 9798
    // 0.587 × 32768 = 19235
    // 0.114 × 32768 = 3735

    /// <summary>Коэффициент R (0.299) в Q15.</summary>
    public const short CoefficientR_Q15 = 9798;

    /// <summary>Коэффициент G (0.587) в Q15.</summary>
    public const short CoefficientG_Q15 = 19235;

    /// <summary>Коэффициент B (0.114) в Q15.</summary>
    public const short CoefficientB_Q15 = 3735;

    #endregion

    #region ITU-R BT.601 Q16 Coefficients (×65536)

    // Q16: × 65536
    // 0.299 × 65536 = 19595
    // 0.587 × 65536 = 38470
    // 0.114 × 65536 = 7471

    /// <summary>Коэффициент R (0.299) в Q16.</summary>
    public const int CoefficientR_Q16 = 19595;

    /// <summary>Коэффициент G (0.587) в Q16.</summary>
    public const int CoefficientG_Q16 = 38470;

    /// <summary>Коэффициент B (0.114) в Q16.</summary>
    public const int CoefficientB_Q16 = 7471;

    /// <summary>Половина для округления (32768).</summary>
    public const int Half = 32768;

    /// <summary>Половина для округления Q15 (16384).</summary>
    public const short Half_Q15 = 16384;

    /// <summary>Множитель 257 для деления на 255.</summary>
    public const int Mult257 = 257;

    #endregion
}

/// <summary>
/// SSE4.1 векторные константы для Gray8 ↔ RGB конвертации (Vector128).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q15/Q16.
/// </summary>
internal static class Gray8Sse41Vectors
{
    #region Q16 Coefficients for int32 math (×65536)

    /// <summary>Коэффициент R (0.299) в Q16 для SSE int32.</summary>
    public static Vector128<int> CoefficientR_Q16 { get; } = Vector128.Create(Gray8Scalars.CoefficientR_Q16);

    /// <summary>Коэффициент G (0.587) в Q16 для SSE int32.</summary>
    public static Vector128<int> CoefficientG_Q16 { get; } = Vector128.Create(Gray8Scalars.CoefficientG_Q16);

    /// <summary>Коэффициент B (0.114) в Q16 для SSE int32.</summary>
    public static Vector128<int> CoefficientB_Q16 { get; } = Vector128.Create(Gray8Scalars.CoefficientB_Q16);

    /// <summary>Половина для округления (32768).</summary>
    public static Vector128<int> Half { get; } = Vector128.Create(Gray8Scalars.Half);

    #endregion

    #region Q15 Coefficients for int16 math (×32768)

    /// <summary>Коэффициент R (0.299) в Q15 для SSE int16.</summary>
    public static Vector128<short> CoefficientR_Q15 { get; } = Vector128.Create(Gray8Scalars.CoefficientR_Q15);

    /// <summary>Коэффициент G (0.587) в Q15 для SSE int16.</summary>
    public static Vector128<short> CoefficientG_Q15 { get; } = Vector128.Create(Gray8Scalars.CoefficientG_Q15);

    /// <summary>Коэффициент B (0.114) в Q15 для SSE int16.</summary>
    public static Vector128<short> CoefficientB_Q15 { get; } = Vector128.Create(Gray8Scalars.CoefficientB_Q15);

    #endregion

    #region PMADDUBSW Coefficients (Q7, ×128)

    // Q7 коэффициенты для PMADDUBSW (signed int8):
    // 0.299 × 128 = 38.27 ≈ 38
    // 0.587 × 128 = 75.14 ≈ 75
    // 0.114 × 128 = 14.59 ≈ 15
    // Сумма: 38 + 75 + 15 = 128 (идеально для >> 7)

    /// <summary>
    /// PMADDUBSW коэффициенты для RGBA→Gray: [cR, cG, cB, 0] × 4.
    /// Формат: signed bytes для умножения с RGBA порядком.
    /// </summary>
    public static Vector128<sbyte> PmaddubswCoeffsRgba { get; } = Vector128.Create(
        38, 75, 15, 0, // пиксель 0: R×38 + G×75, B×15 + A×0
        38, 75, 15, 0, // пиксель 1
        38, 75, 15, 0, // пиксель 2
        38, 75, 15, 0).AsSByte(); // пиксель 3

    /// <summary>
    /// PMADDUBSW коэффициенты для BGRA→Gray: [cB, cG, cR, 0] × 4.
    /// </summary>
    public static Vector128<sbyte> PmaddubswCoeffsBgra { get; } = Vector128.Create(
        15, 75, 38, 0, // пиксель 0: B×15 + G×75, R×38 + A×0
        15, 75, 38, 0,
        15, 75, 38, 0,
        15, 75, 38, 0).AsSByte();

    /// <summary>
    /// Rounding bias 64 для Q7 округления (добавляется перед >>7).
    /// Обеспечивает round-to-nearest вместо truncation.
    /// </summary>
    public static Vector128<short> RoundingBias64 { get; } = Vector128.Create((short)64);

    /// <summary>
    /// Rounding bias 64 в int32 для Q7 округления.
    /// Используется с PMADDWD pipeline.
    /// </summary>
    public static Vector128<int> RoundingBias64_Int32 { get; } = Vector128.Create(64);

    /// <summary>
    /// Коэффициенты [1,1,1,1,1,1,1,1] для PMADDWD.
    /// Суммирует пары int16 → int32.
    /// </summary>
    public static Vector128<short> Ones16 { get; } = Vector128.Create((short)1);

    #endregion

    #region Shuffle Masks - Gray to RGBA/BGRA (Y → YYYY with alpha)

    /// <summary>Shuffle: Y0Y0Y0_, Y1Y1Y1_, Y2Y2Y2_, Y3Y3Y3_ (4 пикселя).</summary>
    public static Vector128<byte> ShuffleGrayToRgba { get; } = Vector128.Create<byte>(
        [0, 0, 0, 0x80, 1, 1, 1, 0x80, 2, 2, 2, 0x80, 3, 3, 3, 0x80]);

    /// <summary>Маска альфа-канала 255 для RGBA/BGRA.</summary>
    public static Vector128<byte> AlphaMask255 { get; } = Vector128.Create<byte>(
        [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255]);

    #endregion

    #region Shuffle Masks - Gray to RGB24/BGR24 (16 пикселей → 48 байт)

    /// <summary>Байты 0-15: пиксели 0-5 → [Y0,Y0,Y0,Y1,Y1,Y1,Y2,Y2,Y2,Y3,Y3,Y3,Y4,Y4,Y4,Y5].</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_0 { get; } = Vector128.Create(
        (byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5);

    /// <summary>Байты 16-31 (low half): пиксели 5-7 → [Y5,Y5,Y6,Y6,Y6,Y7,Y7,Y7,0,0,0,0,0,0,0,0].</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_16_31_Lo { get; } = Vector128.Create<byte>(
        [5, 5, 6, 6, 6, 7, 7, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);

    /// <summary>Байты 16-31 (high half): пиксели 8-10 → [0,0,0,0,0,0,0,0,Y8,Y8,Y8,Y9,Y9,Y9,Y10,Y10].</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_16_31_Hi { get; } = Vector128.Create<byte>(
        [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 8, 8, 8, 9, 9, 9, 10, 10]);

    /// <summary>Байты 32-47: пиксели 10-15 → [Y10,Y11,Y11,Y11,Y12,Y12,Y12,Y13,Y13,Y13,Y14,Y14,Y14,Y15,Y15,Y15].</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_32_47 { get; } = Vector128.Create(
        (byte)10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15);

    /// <summary>Пиксели 5-7 → байты 16-23 + padding (legacy).</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_1 { get; } = Vector128.Create(
        5, 5, 6, 6, 6, 7, 7, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Padding + пиксели 8-10 → байты 24-39 (legacy).</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0, 0, 0, 1, 1, 1, 2, 2);

    /// <summary>Пиксели 10-15 → байты 32-47 (legacy).</summary>
    public static Vector128<byte> ShuffleGrayToRgb24_3 { get; } = Vector128.Create(
        (byte)2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7);

    #endregion

    #region Shuffle Masks - RGBA/BGRA to Gray (деинтерливинг)

    /// <summary>Деинтерливинг R из RGBA: R0,R1,R2,R3 (байты 0,4,8,12).</summary>
    public static Vector128<byte> ShuffleRgbaToR { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг G из RGBA: G0,G1,G2,G3 (байты 1,5,9,13).</summary>
    public static Vector128<byte> ShuffleRgbaToG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг B из RGBA: B0,B1,B2,B3 (байты 2,6,10,14).</summary>
    public static Vector128<byte> ShuffleRgbaToB { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг R из RGBA (8 пикселей): R0-R3 в байты 0,2,4,6 + R4-R7 в байты 8,10,12,14.</summary>
    /// <remarks>Для использования с 2x Vector128 RGBA: первый Vector128 даёт байты 0-7, второй — 8-15.</remarks>
    public static Vector128<byte> ShuffleRgbaToR_8px_Lo { get; } = Vector128.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг R из второго RGBA Vector128 (пиксели 4-7 в байты 8-15).</summary>
    public static Vector128<byte> ShuffleRgbaToR_8px_Hi { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80);

    /// <summary>Деинтерливинг G из RGBA (8 пикселей): G0-G3 в байты 0,2,4,6.</summary>
    public static Vector128<byte> ShuffleRgbaToG_8px_Lo { get; } = Vector128.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг G из второго RGBA Vector128 (пиксели 4-7 в байты 8-15).</summary>
    public static Vector128<byte> ShuffleRgbaToG_8px_Hi { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80);

    /// <summary>Деинтерливинг B из RGBA (8 пикселей): B0-B3 в байты 0,2,4,6.</summary>
    public static Vector128<byte> ShuffleRgbaToB_8px_Lo { get; } = Vector128.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг B из второго RGBA Vector128 (пиксели 4-7 в байты 8-15).</summary>
    public static Vector128<byte> ShuffleRgbaToB_8px_Hi { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80);

    /// <summary>Деинтерливинг B из BGRA: B0,B1,B2,B3 (байты 0,4,8,12).</summary>
    public static Vector128<byte> ShuffleBgraToB { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг G из BGRA: G0,G1,G2,G3 (байты 1,5,9,13).</summary>
    public static Vector128<byte> ShuffleBgraToG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг R из BGRA: R0,R1,R2,R3 (байты 2,6,10,14).</summary>
    public static Vector128<byte> ShuffleBgraToR { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region RGB24 Deinterleave Shuffle Masks (24 байта = 8 триплетов → R, G, B)

    // RGB24: R0G0B0 R1G1B1 R2G2B2 R3G3B3 R4G4B4 R5G5B5 R6G6B6 R7G7B7
    // Позиции: R=0,3,6,9,12,15,18,21; G=1,4,7,10,13,16,19,22; B=2,5,8,11,14,17,20,23

    /// <summary>Извлечение R из bytes0 (16 байт): R0=0, R1=3, R2=6, R3=9, R4=12, R5=15.</summary>
    public static Vector128<byte> ShuffleRgb24ToR0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из bytes1 (8 байт): R6=2, R7=5 (позиции 18,21 mod 16).</summary>
    public static Vector128<byte> ShuffleRgb24ToR1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes0: G0=1, G1=4, G2=7, G3=10, G4=13.</summary>
    public static Vector128<byte> ShuffleRgb24ToG0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes1: G5=0, G6=3, G7=6 (позиции 16,19,22 mod 16).</summary>
    public static Vector128<byte> ShuffleRgb24ToG1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из bytes0: B0=2, B1=5, B2=8, B3=11, B4=14.</summary>
    public static Vector128<byte> ShuffleRgb24ToB0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из bytes1: B5=1, B6=4, B7=7 (позиции 17,20,23 mod 16).</summary>
    public static Vector128<byte> ShuffleRgb24ToB1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region RGB24 → RGBA32 Shuffle Masks (для PMADDUBSW)

    // RGB24 (12 байт = 4 пикселя): R0G0B0 R1G1B1 R2G2B2 R3G3B3
    // → RGBA32: R0G0B0_ R1G1B1_ R2G2B2_ R3G3B3_ (16 байт, _ = 0x80 → 0)
    // Входные позиции: 0,1,2, 3,4,5, 6,7,8, 9,10,11

    /// <summary>
    /// Shuffle RGB24 (12 байт = 4 пикселя) → RGBA32 формат с нулевым padding.
    /// [R0,G0,B0,R1,G1,B1,R2,G2,B2,R3,G3,B3,x,x,x,x] → [R0,G0,B0,0, R1,G1,B1,0, R2,G2,B2,0, R3,G3,B3,0]
    /// </summary>
    public static Vector128<byte> ShuffleRgb24ToRgba32 { get; } = Vector128.Create(
        0, 1, 2, 0x80,    // пиксель 0: R0,G0,B0,0
        3, 4, 5, 0x80,    // пиксель 1: R1,G1,B1,0
        6, 7, 8, 0x80,    // пиксель 2: R2,G2,B2,0
        9, 10, 11, 0x80); // пиксель 3: R3,G3,B3,0

    #endregion

    #region BGR24 Deinterleave Shuffle Masks (24 байта = 8 триплетов → B, G, R)

    // BGR24: B0G0R0 B1G1R1 B2G2R2 ... (B=байты 0,3,6...; G=1,4,7...; R=2,5,8...)
    // Эти маски идентичны RGB24, только с переименованием B↔R

    /// <summary>Извлечение B из bytes0 (BGR24): B0=0, B1=3, B2=6, B3=9, B4=12, B5=15.</summary>
    public static Vector128<byte> ShuffleBgr24ToB0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из bytes1 (BGR24): B6=2, B7=5.</summary>
    public static Vector128<byte> ShuffleBgr24ToB1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes0 (BGR24): G0=1, G1=4, G2=7, G3=10, G4=13.</summary>
    public static Vector128<byte> ShuffleBgr24ToG0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes1 (BGR24): G5=0, G6=3, G7=6.</summary>
    public static Vector128<byte> ShuffleBgr24ToG1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из bytes0 (BGR24): R0=2, R1=5, R2=8, R3=11, R4=14.</summary>
    public static Vector128<byte> ShuffleBgr24ToR0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из bytes1 (BGR24): R5=1, R6=4, R7=7.</summary>
    public static Vector128<byte> ShuffleBgr24ToR1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region CMYK ↔ Gray8 Constants

    /// <summary>Вектор 255 для SSE.</summary>
    public static Vector128<byte> AllFF { get; } = Vector128.Create((byte)255);

    #endregion

    #region CMYK → Gray8 Shuffle Masks (извлечение K, SSE)

    /// <summary>
    /// SSE: Извлечение K из 16 байт CMYK (4 пикселя).
    /// K находится в позициях 3, 7, 11, 15 → первые 4 байта.
    /// </summary>
    public static Vector128<byte> ShuffleCmykToK { get; } = Vector128.Create(
        3, 7, 11, 15,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// SSE: Извлечение K из 16 байт CMYK (4 пикселя) → байты 0-3.
    /// Для Or-based сборки: K в позициях 0-3.
    /// </summary>
    public static Vector128<byte> ShuffleCmykToK_Pos0 { get; } = Vector128.Create(
        3, 7, 11, 15,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// SSE: Извлечение K из 16 байт CMYK (4 пикселя) → байты 4-7.
    /// Для Or-based сборки: K в позициях 4-7.
    /// </summary>
    public static Vector128<byte> ShuffleCmykToK_Pos1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// SSE: Извлечение K из 16 байт CMYK (4 пикселя) → байты 8-11.
    /// Для Or-based сборки: K в позициях 8-11.
    /// </summary>
    public static Vector128<byte> ShuffleCmykToK_Pos2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15,
        0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// SSE: Извлечение K из 16 байт CMYK (4 пикселя) → байты 12-15.
    /// Для Or-based сборки: K в позициях 12-15.
    /// </summary>
    public static Vector128<byte> ShuffleCmykToK_Pos3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15);

    #endregion

    #region Gray8 → CMYK Shuffle Masks (расширение Gray → [0,0,0,K])

    /// <summary>
    /// SSE: Расширение 4 байт Gray в 16 байт CMYK.
    /// Gray[0,1,2,3] → [0,0,0,G0, 0,0,0,G1, 0,0,0,G2, 0,0,0,G3].
    /// </summary>
    public static Vector128<byte> ShuffleGrayToCmyk0 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0,
        0x80, 0x80, 0x80, 1,
        0x80, 0x80, 0x80, 2,
        0x80, 0x80, 0x80, 3);

    /// <summary>SSE: Gray[4-7] → CMYK.</summary>
    public static Vector128<byte> ShuffleGrayToCmyk1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 4,
        0x80, 0x80, 0x80, 5,
        0x80, 0x80, 0x80, 6,
        0x80, 0x80, 0x80, 7);

    /// <summary>SSE: Gray[8-11] → CMYK.</summary>
    public static Vector128<byte> ShuffleGrayToCmyk2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 8,
        0x80, 0x80, 0x80, 9,
        0x80, 0x80, 0x80, 10,
        0x80, 0x80, 0x80, 11);

    /// <summary>SSE: Gray[12-15] → CMYK.</summary>
    public static Vector128<byte> ShuffleGrayToCmyk3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 12,
        0x80, 0x80, 0x80, 13,
        0x80, 0x80, 0x80, 14,
        0x80, 0x80, 0x80, 15);

    #endregion

    #region YCoCgR32 Shuffle Masks

    /// <summary>Нейтральное значение 127 (для Co=0, Cg=0).</summary>
    public static Vector128<byte> Neutral127 { get; } = Vector128.Create((byte)127);

    /// <summary>Нейтральное значение 3 (Frac для lossless).</summary>
    public static Vector128<byte> Neutral3 { get; } = Vector128.Create((byte)3);

    /// <summary>Shuffle для извлечения Y (первый байт каждого 4-byte пикселя).</summary>
    public static Vector128<byte> ShuffleYCoCgR32ToY { get; } = Vector128.Create(
        0, 4, 8, 12,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Hsv Shuffle Masks

    /// <summary>
    /// Gray8 → Hsv: G0 → (0,0,0,G0), G1 → (0,0,0,G1), G2 → (0,0,0,G2), G3 → (0,0,0,G3).
    /// </summary>
    public static Vector128<byte> ShuffleGrayToHsv { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0,    // pixel 0: [0, 0, 0, G0]
        0x80, 0x80, 0x80, 1,          // pixel 1: [0, 0, 0, G1]
        0x80, 0x80, 0x80, 2,          // pixel 2: [0, 0, 0, G2]
        0x80, 0x80, 0x80, 3);         // pixel 3: [0, 0, 0, G3]

    /// <summary>
    /// Hsv → Gray8: Извлекаем V из позиций 3, 7, 11, 15 (каждый 4-й байт).
    /// </summary>
    public static Vector128<byte> ShuffleHsvToV { get; } = Vector128.Create(
        3, 7, 11, 15,           // V из 4 пикселей
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80);

    #endregion

    #region YCbCr Masks (Gray8 → YCbCr)

    /// <summary>Маска для Cb/Cr значений 128: паттерн [0, 128, 128, 0, 128, 128, ...].</summary>
    public static Vector128<byte> CbCrMask { get; } = Vector128.Create(
        0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0);

    /// <summary>Маска для Y значений (0xFF в позициях 0,3,6,9,12,15).</summary>
    public static Vector128<byte> YMask { get; } = Vector128.Create(
        0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF);

    /// <summary>Маска для Cb/Cr значений 128, паттерн 2 (сдвинутый).</summary>
    public static Vector128<byte> CbCrMask2 { get; } = Vector128.Create(
        128, 128, 0, 128, 128, 0, 128, 128, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Маска для Y значений, паттерн 2 (позиции 2,5).</summary>
    public static Vector128<byte> YMask2 { get; } = Vector128.Create(
        0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Маска для Cb/Cr значений 128, паттерн 3 (для склейки средней части).</summary>
    public static Vector128<byte> CbCrMask3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0, 128, 128, 0, 128, 128, 0, 128);

    /// <summary>Маска для Y значений, паттерн 3 (для склейки средней части).</summary>
    public static Vector128<byte> YMask3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0);

    /// <summary>Маска для Cb/Cr значений 128, паттерн 4 (финальный блок).</summary>
    public static Vector128<byte> CbCrMask4 { get; } = Vector128.Create(
        128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128);

    /// <summary>Маска для Y значений, паттерн 4 (финальный блок).</summary>
    public static Vector128<byte> YMask4 { get; } = Vector128.Create(
        0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для Gray8 ↔ RGB конвертации (Vector256).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q15/Q16.
/// </summary>
internal static class Gray8Avx2Vectors
{
    #region PMADDUBSW Coefficients

    /// <summary>
    /// VPMADDUBSW коэффициенты Q7 для RGBA→Gray: [cR, cG, cB, 0] × 8.
    /// Y = (38×R + 75×G + 15×B) >> 7.
    /// КРИТИЧНО: все коэффициенты &lt;128, помещаются в sbyte!
    /// </summary>
    public static Vector256<sbyte> PmaddubswCoeffsRgba { get; } = Vector256.Create(
        38, 75, 15, 0, 38, 75, 15, 0,
        38, 75, 15, 0, 38, 75, 15, 0,
        38, 75, 15, 0, 38, 75, 15, 0,
        38, 75, 15, 0, 38, 75, 15, 0);

    /// <summary>
    /// VPMADDUBSW коэффициенты Q7 для BGRA→Gray: [cB, cG, cR, 0] × 8.
    /// Y = (38×R + 75×G + 15×B) >> 7.
    /// КРИТИЧНО: все коэффициенты &lt;128, помещаются в sbyte!
    /// </summary>
    public static Vector256<sbyte> PmaddubswCoeffsBgra { get; } = Vector256.Create(
        15, 75, 38, 0, 15, 75, 38, 0,
        15, 75, 38, 0, 15, 75, 38, 0,
        15, 75, 38, 0, 15, 75, 38, 0,
        15, 75, 38, 0, 15, 75, 38, 0);

    /// <summary>
    /// Rounding bias 64 для Q7 округления (добавляется перед >>7).
    /// Обеспечивает round-to-nearest вместо truncation.
    /// </summary>
    public static Vector256<short> RoundingBias64 { get; } = Vector256.Create((short)64);

    /// <summary>
    /// Rounding bias 64 в int32 для Q7 округления.
    /// Используется с VPMADDWD pipeline.
    /// </summary>
    public static Vector256<int> RoundingBias64_Int32 { get; } = Vector256.Create(64);

    /// <summary>
    /// Коэффициенты [1,1,...] для VPMADDWD.
    /// Суммирует пары int16 → int32.
    /// </summary>
    public static Vector256<short> Ones16 { get; } = Vector256.Create((short)1);

    /// <summary>
    /// VPERMQ маска для упорядочивания результатов после VPACKUSWB.
    /// AVX2 VPACKUSWB работает in-lane, нарушая порядок байтов.
    /// После pack: [0-7:lane0, 8-15:lane1, 16-23:lane0', 24-31:lane1']
    /// Нужно: [0-7:lane0, 16-23:lane0', 8-15:lane1, 24-31:lane1']
    /// VPERMQ с mask 0b11_01_10_00 = {0,2,1,3} исправляет порядок.
    /// </summary>
    public static Vector256<long> PermutePackedGray { get; } = Vector256.Create(0L, 2L, 1L, 3L);

    #endregion

    #region Q15 Coefficients (×32768)

    /// <summary>Коэффициент R (0.299) в Q15 для AVX2.</summary>
    public static Vector256<short> CoefficientR { get; } = Vector256.Create(Gray8Scalars.CoefficientR_Q15);

    /// <summary>Коэффициент G (0.587) в Q15 для AVX2.</summary>
    public static Vector256<short> CoefficientG { get; } = Vector256.Create(Gray8Scalars.CoefficientG_Q15);

    /// <summary>Коэффициент B (0.114) в Q15 для AVX2.</summary>
    public static Vector256<short> CoefficientB { get; } = Vector256.Create(Gray8Scalars.CoefficientB_Q15);

    /// <summary>Половина для округления AVX2 (16384 = 0.5 в Q15).</summary>
    public static Vector256<short> Half { get; } = Vector256.Create(Gray8Scalars.Half_Q15);

    #endregion

    #region Q16 Coefficients (×65536 для int32 математики)

    /// <summary>Коэффициент R (0.299) в Q16 для AVX2 int32.</summary>
    public static Vector256<int> CoefficientR_Q16 { get; } = Vector256.Create(Gray8Scalars.CoefficientR_Q16);

    /// <summary>Коэффициент G (0.587) в Q16 для AVX2 int32.</summary>
    public static Vector256<int> CoefficientG_Q16 { get; } = Vector256.Create(Gray8Scalars.CoefficientG_Q16);

    /// <summary>Коэффициент B (0.114) в Q16 для AVX2 int32.</summary>
    public static Vector256<int> CoefficientB_Q16 { get; } = Vector256.Create(Gray8Scalars.CoefficientB_Q16);

    /// <summary>Половина для округления Q16 (32768).</summary>
    public static Vector256<int> Half_Q16 { get; } = Vector256.Create(Gray8Scalars.Half);

    /// <summary>Множитель 257 для деления на 255: (x * 257 + 128) >> 16 ≈ x / 255.</summary>
    public static Vector256<int> Mult257 { get; } = Vector256.Create(Gray8Scalars.Mult257);

    #endregion

    #region CMYK Deinterleave Shuffle Masks

    // CMYK: C0M0Y0K0 C1M1Y1K1 ... (каждый пиксель 4 байта)
    // Для 8 пикселей (32 байт) извлекаем C, M, Y, K по 8 значений

    /// <summary>Извлечение C из 32 байт CMYK (байты 0,4,8,12,16,20,24,28 → нижние 8 байт).</summary>
    public static Vector256<byte> ShuffleCmykToC { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение M из 32 байт CMYK.</summary>
    public static Vector256<byte> ShuffleCmykToM { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Y из 32 байт CMYK.</summary>
    public static Vector256<byte> ShuffleCmykToY { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение K из 32 байт CMYK.</summary>
    public static Vector256<byte> ShuffleCmykToK { get; } = Vector256.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// AVX2: Извлечение K из 32 байт CMYK (8 пикселей) → байты 0-3 каждой lane.
    /// Для Or-based сборки первого блока.
    /// </summary>
    public static Vector256<byte> ShuffleCmykToK_Pos0 { get; } = Vector256.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// AVX2: Извлечение K из 32 байт CMYK (8 пикселей) → байты 4-7 каждой lane.
    /// Для Or-based сборки второго блока.
    /// </summary>
    public static Vector256<byte> ShuffleCmykToK_Pos1 { get; } = Vector256.Create(
        0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// AVX2: Извлечение K из 32 байт CMYK (8 пикселей) → байты 8-11 каждой lane.
    /// Для Or-based сборки третьего блока.
    /// </summary>
    public static Vector256<byte> ShuffleCmykToK_Pos2 { get; } = Vector256.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// AVX2: Извлечение K из 32 байт CMYK (8 пикселей) → байты 12-15 каждой lane.
    /// Для Or-based сборки четвёртого блока.
    /// </summary>
    public static Vector256<byte> ShuffleCmykToK_Pos3 { get; } = Vector256.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 3, 7, 11, 15);

    /// <summary>
    /// AVX2 permute индексы для сборки K из CMYK.
    /// Вход: [K0-3 K8-11 K16-19 K24-27 | K4-7 K12-15 K20-23 K28-31] (после Or+Shift)
    /// Выход: [K0-3 K4-7 K8-11 K12-15 K16-19 K20-23 K24-27 K28-31]
    /// Permute: {0,4,1,5,2,6,3,7}
    /// </summary>
    public static Vector256<int> PermuteCmykToK { get; } = Vector256.Create(0, 4, 1, 5, 2, 6, 3, 7);

    #endregion

    #region AVX2 Shuffle Masks (Gray → RGBA/BGRA, 8 пикселей → 32 байт)

    /// <summary>AVX2: Y0-Y7 → RGBA (in-lane, lower 8 bytes → lower 32 bytes).</summary>
    public static Vector256<byte> ShuffleGrayToRgba_Lo { get; } = Vector256.Create(
        0, 0, 0, 0x80, 1, 1, 1, 0x80, 2, 2, 2, 0x80, 3, 3, 3, 0x80,
        0, 0, 0, 0x80, 1, 1, 1, 0x80, 2, 2, 2, 0x80, 3, 3, 3, 0x80);

    /// <summary>AVX2: Y4-Y7 → RGBA (in-lane, bytes 4-7 → expanded).</summary>
    public static Vector256<byte> ShuffleGrayToRgba_Hi { get; } = Vector256.Create(
        4, 4, 4, 0x80, 5, 5, 5, 0x80, 6, 6, 6, 0x80, 7, 7, 7, 0x80,
        4, 4, 4, 0x80, 5, 5, 5, 0x80, 6, 6, 6, 0x80, 7, 7, 7, 0x80);

    /// <summary>AVX2 маска альфа 255.</summary>
    public static Vector256<byte> AlphaMask255 { get; } = Vector256.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion

    #region AVX2 Shuffle Masks (RGBA/BGRA → Gray, деинтерливинг 8 пикселей)

    /// <summary>AVX2 деинтерливинг R из RGBA (байты 0,4,8,12,16,20,24,28 → 0-7).</summary>
    public static Vector256<byte> ShuffleRgbaToR { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>AVX2 деинтерливинг G из RGBA.</summary>
    public static Vector256<byte> ShuffleRgbaToG { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>AVX2 деинтерливинг B из RGBA.</summary>
    public static Vector256<byte> ShuffleRgbaToB { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>AVX2 деинтерливинг B из BGRA.</summary>
    public static Vector256<byte> ShuffleBgraToB { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>AVX2 деинтерливинг G из BGRA.</summary>
    public static Vector256<byte> ShuffleBgraToG { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>AVX2 деинтерливинг R из BGRA.</summary>
    public static Vector256<byte> ShuffleBgraToR { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region CMYK ↔ Gray8 Constants (256-bit)

    /// <summary>Вектор 255 для AVX2.</summary>
    public static Vector256<byte> AllFF { get; } = Vector256.Create((byte)255);

    #endregion

    #region AVX2 Gray8 → CMYK Shuffle Masks (расширение Gray → [0,0,0,K], 256-bit)

    /// <summary>
    /// AVX2: Расширение Gray[0-3] и Gray[8-11] → CMYK (in-lane).
    /// Каждая lane: 4 Gray → 16 байт CMYK [0,0,0,K].
    /// </summary>
    public static Vector256<byte> ShuffleGrayToCmyk0 { get; } = Vector256.Create(
        0x80, 0x80, 0x80, 0, 0x80, 0x80, 0x80, 1, 0x80, 0x80, 0x80, 2, 0x80, 0x80, 0x80, 3,
        0x80, 0x80, 0x80, 0, 0x80, 0x80, 0x80, 1, 0x80, 0x80, 0x80, 2, 0x80, 0x80, 0x80, 3);

    /// <summary>AVX2: Gray[4-7] и Gray[12-15] → CMYK (in-lane).</summary>
    public static Vector256<byte> ShuffleGrayToCmyk1 { get; } = Vector256.Create(
        0x80, 0x80, 0x80, 4, 0x80, 0x80, 0x80, 5, 0x80, 0x80, 0x80, 6, 0x80, 0x80, 0x80, 7,
        0x80, 0x80, 0x80, 4, 0x80, 0x80, 0x80, 5, 0x80, 0x80, 0x80, 6, 0x80, 0x80, 0x80, 7);

    #endregion

    #region AVX2 CMYK → Gray8 Shuffle Masks (извлечение K, 256-bit)

    /// <summary>
    /// AVX2: Извлечение K из 32 байт CMYK (8 пикселей, in-lane).
    /// K в позициях 3,7,11,15 каждой lane → первые 4 байта каждой lane.
    /// </summary>
    public static Vector256<byte> ShuffleCmykToK_256 { get; } = Vector256.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion
}
