using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для Bgra32 (Vector128).
/// </summary>
internal static class Bgra32Sse41Vectors
{
    #region BGRA ↔ RGBA Swap Masks

    /// <summary>
    /// Swap R и B в 32-bit пикселях (BGRA ↔ RGBA).
    /// [2,1,0,3, 6,5,4,7, 10,9,8,11, 14,13,12,15]
    /// </summary>
    public static Vector128<byte> SwapRB32Mask { get; } = Vector128.Create(
        (byte)2, 1, 0, 3,      // пиксель 0
        6, 5, 4, 7,            // пиксель 1
        10, 9, 8, 11,          // пиксель 2
        14, 13, 12, 15);       // пиксель 3

    #endregion

    #region BGR24 ↔ BGRA32 Shuffle Masks

    /// <summary>
    /// BGR24 → BGRA32: добавление альфа-канала (без swap).
    /// B0G0R0 B1G1R1 ... → B0G0R0A0 B1G1R1A1 ...
    /// </summary>
    public static Vector128<byte> Bgr24ToBgra32ShuffleMask { get; } = Vector128.Create(
        0, 1, 2, 0x80,   // пиксель 0
        3, 4, 5, 0x80,         // пиксель 1
        6, 7, 8, 0x80,         // пиксель 2
        9, 10, 11, 0x80);      // пиксель 3

    /// <summary>
    /// BGRA32 → BGR24: удаление альфа-канала (без swap).
    /// B0G0R0A0 B1G1R1A1 ... → B0G0R0 B1G1R1 ...
    /// </summary>
    public static Vector128<byte> Bgra32ToBgr24ShuffleMask { get; } = Vector128.Create(
        0, 1, 2,         // пиксель 0
        4, 5, 6,               // пиксель 1
        8, 9, 10,              // пиксель 2
        12, 13, 14,            // пиксель 3
        0x80, 0x80, 0x80, 0x80);

    #endregion

    #region RGB24 ↔ BGRA32 Shuffle Masks

    /// <summary>
    /// RGB24 → BGRA32: swap R↔B + добавление альфа.
    /// R0G0B0 R1G1B1 ... → B0G0R0A0 B1G1R1A1 ...
    /// </summary>
    public static Vector128<byte> Rgb24ToBgra32ShuffleMask { get; } = Vector128.Create(
        2, 1, 0, 0x80,   // пиксель 0
        5, 4, 3, 0x80,         // пиксель 1
        8, 7, 6, 0x80,         // пиксель 2
        11, 10, 9, 0x80);      // пиксель 3

    /// <summary>
    /// BGRA32 → RGB24: swap B↔R + удаление альфа.
    /// B0G0R0A0 B1G1R1A1 ... → R0G0B0 R1G1B1 ...
    /// </summary>
    public static Vector128<byte> Bgra32ToRgb24ShuffleMask { get; } = Vector128.Create(
        2, 1, 0,         // пиксель 0
        6, 5, 4,               // пиксель 1
        10, 9, 8,              // пиксель 2
        14, 13, 12,            // пиксель 3
        0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (4 пикселя).</summary>
    public static Vector128<byte> Alpha255Mask { get; } = Vector128.Create(
        0, 0, 0, 255,
        0, 0, 0, 255,
        0, 0, 0, 255,
        0, 0, 0, 255);

    /// <summary>Вектор из 255 для альфа-канала (byte).</summary>
    public static Vector128<byte> Alpha255 { get; } = Vector128.Create((byte)255);

    #endregion

    #region BGRA Deinterleave Masks (YCbCr)

    /// <summary>Извлечение B компонентов из 4 пикселей BGRA.</summary>
    public static Vector128<byte> ExtractBMask { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G компонентов из 4 пикселей BGRA.</summary>
    public static Vector128<byte> ExtractGMask { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R компонентов из 4 пикселей BGRA.</summary>
    public static Vector128<byte> ExtractRMask { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region BT.601 YCbCr Constants (Q16)

    // RGB → YCbCr
    public static Vector128<int> CYR { get; } = Vector128.Create(19595);    // 0.299 * 65536
    public static Vector128<int> CYG { get; } = Vector128.Create(38470);    // 0.587 * 65536
    public static Vector128<int> CYB { get; } = Vector128.Create(7471);     // 0.114 * 65536
    public static Vector128<int> CCbR { get; } = Vector128.Create(-11056);  // -0.168736 * 65536
    public static Vector128<int> CCbG { get; } = Vector128.Create(-21712);  // -0.331264 * 65536
    public static Vector128<int> CCbB { get; } = Vector128.Create(32768);   // 0.5 * 65536
    public static Vector128<int> CCrR { get; } = Vector128.Create(32768);   // 0.5 * 65536
    public static Vector128<int> CCrG { get; } = Vector128.Create(-27440);  // -0.418688 * 65536
    public static Vector128<int> CCrB { get; } = Vector128.Create(-5328);   // -0.081312 * 65536

    // YCbCr → RGB
    public static Vector128<int> C1402 { get; } = Vector128.Create(91881);   // 1.402 * 65536
    public static Vector128<int> C0344 { get; } = Vector128.Create(22554);   // 0.344136 * 65536
    public static Vector128<int> C0714 { get; } = Vector128.Create(46802);   // 0.714136 * 65536
    public static Vector128<int> C1772 { get; } = Vector128.Create(116130);  // 1.772 * 65536

    // Common
    public static Vector128<int> Offset128 { get; } = Vector128.Create(128);
    public static Vector128<int> HalfQ16 { get; } = Vector128.Create(32768);

    #endregion

    #region YCbCr Interleave/Deinterleave Masks

    /// <summary>
    /// YCb → packed interleave (первые 16 байт из 24).
    /// UnpackLow(Y,Cb) = Y0Cb0 Y1Cb1 Y2Cb2 Y3Cb3 Y4Cb4 Y5Cb5 Y6Cb6 Y7Cb7
    /// Нужно: Y0Cb0Cr0 Y1Cb1Cr1...
    /// </summary>
    public static Vector128<byte> YCbToYCbCrShuffleMask0 { get; } = Vector128.Create(
        0, 1, 0x80,  // Y0 Cb0 Cr0
        2, 3, 0x80,  // Y1 Cb1 Cr1
        4, 5, 0x80,  // Y2 Cb2 Cr2
        6, 7, 0x80,  // Y3 Cb3 Cr3
        8, 9, 0x80,  // Y4 Cb4 Cr4
        10);         // Y5 (partial)

    /// <summary>Cr → первые 16 байт.</summary>
    public static Vector128<byte> CrToYCbCrShuffleMask0 { get; } = Vector128.Create(
        0x80, 0x80, 0,   // Cr0
        0x80, 0x80, 1,   // Cr1
        0x80, 0x80, 2,   // Cr2
        0x80, 0x80, 3,   // Cr3
        0x80, 0x80, 4,   // Cr4
        0x80);           // (padding)

    /// <summary>YCb → оставшиеся 8 байт.</summary>
    public static Vector128<byte> YCbToYCbCrShuffleMask1 { get; } = Vector128.Create(
        11, 0x80,        // Cb5 Cr5
        12, 13, 0x80,    // Y6 Cb6 Cr6
        14, 15, 0x80,    // Y7 Cb7 Cr7
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Cr → оставшиеся 8 байт.</summary>
    public static Vector128<byte> CrToYCbCrShuffleMask1 { get; } = Vector128.Create(
        0x80, 5,          // Cr5
        0x80, 0x80, 6,    // Cr6
        0x80, 0x80, 7,    // Cr7
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Y из YCbCr (первый блок 16 байт → 6 пикселей Y в позициях 0-5).</summary>
    public static Vector128<byte> YCbCrDeinterleaveY0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Y из YCbCr (второй блок 8 байт → 2 пикселя Y).</summary>
    public static Vector128<byte> YCbCrDeinterleaveY1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Cb из YCbCr (первый блок 16 байт → 5 пикселей Cb в позициях 0-4).</summary>
    public static Vector128<byte> YCbCrDeinterleaveCb0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Cb из YCbCr (второй блок 8 байт → 3 пикселя Cb).</summary>
    public static Vector128<byte> YCbCrDeinterleaveCb1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Cr из YCbCr (первый блок 16 байт → 5 пикселей Cr в позициях 0-4).</summary>
    public static Vector128<byte> YCbCrDeinterleaveCr0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Deinterleave Cr из YCbCr (второй блок 8 байт → 3 пикселя Cr).</summary>
    public static Vector128<byte> YCbCrDeinterleaveCr1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region BT.601 YCbCr Constants (Q15, int16) for MultiplyHighRoundScale

    // RGB → YCbCr (прямое преобразование)
    /// <summary>0.299 × 32768 = 9798</summary>
    public static Vector128<short> Q15CYR { get; } = Vector128.Create((short)9798);

    /// <summary>0.587 × 32768 = 19235</summary>
    public static Vector128<short> Q15CYG { get; } = Vector128.Create((short)19235);

    /// <summary>0.114 × 32768 = 3736</summary>
    public static Vector128<short> Q15CYB { get; } = Vector128.Create((short)3736);

    /// <summary>-0.168736 × 32768 = -5528</summary>
    public static Vector128<short> Q15CCbR { get; } = Vector128.Create((short)-5528);

    /// <summary>-0.331264 × 32768 = -10856</summary>
    public static Vector128<short> Q15CCbG { get; } = Vector128.Create((short)-10856);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector128<short> Q15CCbB { get; } = Vector128.Create((short)16384);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector128<short> Q15CCrR { get; } = Vector128.Create((short)16384);

    /// <summary>-0.418688 × 32768 = -13720</summary>
    public static Vector128<short> Q15CCrG { get; } = Vector128.Create((short)-13720);

    /// <summary>-0.081312 × 32768 = -2664</summary>
    public static Vector128<short> Q15CCrB { get; } = Vector128.Create((short)-2664);

    // YCbCr → RGB (обратное преобразование)
    // Коэффициенты >1.0 разбиты: 1.402 = 1 + 0.402, 1.772 = 1 + 0.772
    /// <summary>0.402 × 32768 = 13172 (для R = Y + Cr + 0.402×Cr)</summary>
    public static Vector128<short> Q15C0402 { get; } = Vector128.Create((short)13172);

    /// <summary>0.344136 × 32768 = 11277</summary>
    public static Vector128<short> Q15C0344 { get; } = Vector128.Create((short)11277);

    /// <summary>0.714136 × 32768 = 23401</summary>
    public static Vector128<short> Q15C0714 { get; } = Vector128.Create((short)23401);

    /// <summary>0.772 × 32768 = 25297 (для B = Y + Cb + 0.772×Cb)</summary>
    public static Vector128<short> Q15C0772 { get; } = Vector128.Create((short)25297);

    // Common
    /// <summary>128 (смещение для Cb/Cr)</summary>
    public static Vector128<short> Q15C128 { get; } = Vector128.Create((short)128);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для Bgra32 (Vector256).
/// </summary>
internal static class Bgra32Avx2Vectors
{
    #region BGRA ↔ RGBA Swap Masks

    /// <summary>
    /// Swap R и B в 32-bit пикселях (AVX2, 8 пикселей).
    /// </summary>
    public static Vector256<byte> SwapRB32Mask { get; } = Vector256.Create(
        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,  // lane 0
        2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);       // lane 1

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (8 пикселей).</summary>
    public static Vector256<byte> Alpha255Mask { get; } = Vector256.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion

    #region BT.601 YCbCr Constants (Q15, int16) for MultiplyHighRoundScale

    // RGB → YCbCr (прямое преобразование)
    /// <summary>0.299 × 32768 = 9798</summary>
    public static Vector256<short> CYR { get; } = Vector256.Create((short)9798);

    /// <summary>0.587 × 32768 = 19235</summary>
    public static Vector256<short> CYG { get; } = Vector256.Create((short)19235);

    /// <summary>0.114 × 32768 = 3736</summary>
    public static Vector256<short> CYB { get; } = Vector256.Create((short)3736);

    /// <summary>-0.168736 × 32768 = -5528</summary>
    public static Vector256<short> CCbR { get; } = Vector256.Create((short)-5528);

    /// <summary>-0.331264 × 32768 = -10856</summary>
    public static Vector256<short> CCbG { get; } = Vector256.Create((short)-10856);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector256<short> CCbB { get; } = Vector256.Create((short)16384);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector256<short> CCrR { get; } = Vector256.Create((short)16384);

    /// <summary>-0.418688 × 32768 = -13720</summary>
    public static Vector256<short> CCrG { get; } = Vector256.Create((short)-13720);

    /// <summary>-0.081312 × 32768 = -2664</summary>
    public static Vector256<short> CCrB { get; } = Vector256.Create((short)-2664);

    // YCbCr → RGB (обратное преобразование)
    // Коэффициенты >1.0 разбиты: 1.402 = 1 + 0.402, 1.772 = 1 + 0.772
    /// <summary>0.402 × 32768 = 13172 (для R = Y + Cr + 0.402×Cr)</summary>
    public static Vector256<short> C0402 { get; } = Vector256.Create((short)13172);

    /// <summary>0.344136 × 32768 = 11277</summary>
    public static Vector256<short> C0344 { get; } = Vector256.Create((short)11277);

    /// <summary>0.714136 × 32768 = 23401</summary>
    public static Vector256<short> C0714 { get; } = Vector256.Create((short)23401);

    /// <summary>0.772 × 32768 = 25297 (для B = Y + Cb + 0.772×Cb)</summary>
    public static Vector256<short> C0772 { get; } = Vector256.Create((short)25297);

    // Common
    /// <summary>128 (смещение для Cb/Cr)</summary>
    public static Vector256<short> C128 { get; } = Vector256.Create((short)128);

    #endregion

    #region BT.601 YCbCr Constants (Q16, int32) for MultiplyLow

    // RGB → YCbCr (прямое преобразование) — идентично scalar
    public static Vector256<int> CYRi32 { get; } = Vector256.Create(19595);     // 0.299 × 65536
    public static Vector256<int> CYGi32 { get; } = Vector256.Create(38470);     // 0.587 × 65536
    public static Vector256<int> CYBi32 { get; } = Vector256.Create(7471);      // 0.114 × 65536
    public static Vector256<int> CCbRi32 { get; } = Vector256.Create(-11056);   // -0.168736 × 65536
    public static Vector256<int> CCbGi32 { get; } = Vector256.Create(-21712);   // -0.331264 × 65536
    public static Vector256<int> CCbBi32 { get; } = Vector256.Create(32768);    // 0.5 × 65536
    public static Vector256<int> CCrRi32 { get; } = Vector256.Create(32768);    // 0.5 × 65536
    public static Vector256<int> CCrGi32 { get; } = Vector256.Create(-27440);   // -0.418688 × 65536
    public static Vector256<int> CCrBi32 { get; } = Vector256.Create(-5328);    // -0.081312 × 65536

    // YCbCr → RGB (обратное преобразование)
    public static Vector256<int> C1402i32 { get; } = Vector256.Create(91881);   // 1.402 × 65536
    public static Vector256<int> C0344i32 { get; } = Vector256.Create(22554);   // 0.344136 × 65536
    public static Vector256<int> C0714i32 { get; } = Vector256.Create(46802);   // 0.714136 × 65536
    public static Vector256<int> C1772i32 { get; } = Vector256.Create(116130);  // 1.772 × 65536

    // Common (int32)
    public static Vector256<int> Offset128i32 { get; } = Vector256.Create(128);
    public static Vector256<int> HalfQ16i32 { get; } = Vector256.Create(32768);

    #endregion

    #region AVX2 BGRA Deinterleave Masks

    // Загрузка: 64 байт BGRA (16 пикселей) = 2×Vector256
    // После VPSHUFB в каждой lane получаем 4 пикселя
    // VPSHUFB работает in-lane (индексы 0-15 в каждой 16-байтной lane)

    /// <summary>
    /// Извлечение B из 8 BGRA пикселей (256-bit):
    /// BGRA|BGRA|BGRA|BGRA|BGRA|BGRA|BGRA|BGRA → B0B1B2B3|xxxx|B4B5B6B7|xxxx
    /// Затем Permute для получения B0B1B2B3B4B5B6B7|xxxxxxxx
    /// </summary>
    public static Vector256<byte> ExtractBMask256 { get; } = Vector256.Create(
        // Lane 0: пиксели 0-3 (индексы 0,4,8,12 = позиции B)
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        // Lane 1: пиксели 4-7 (индексы 0,4,8,12 = позиции B в этой lane)
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из 8 BGRA пикселей.</summary>
    public static Vector256<byte> ExtractGMask256 { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из 8 BGRA пикселей.</summary>
    public static Vector256<byte> ExtractRMask256 { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// Permute маска для объединения результатов из двух lanes после VPSHUFB.
    /// [0,4,1,5] = собираем 32-bit слова: lane0[0], lane1[0], lane0[1], lane1[1]
    /// Это даёт: B0B1B2B3|B4B5B6B7|xxxx|xxxx → B0B1B2B3B4B5B6B7|xxxxxxxx
    /// </summary>
    public static Vector256<int> PermuteBGRMask { get; } = Vector256.Create(0, 4, 1, 5, 2, 6, 3, 7);

    #endregion

    #region AVX2 YCbCr Interleave Masks

    /// <summary>
    /// После pack Y,Cb,Cr в отдельные Vector128 (16 байт каждый):
    /// Y0Y1Y2Y3..Y15, Cb0Cb1..Cb15, Cr0Cr1..Cr15
    /// Нужно interleave в Y0Cb0Cr0|Y1Cb1Cr1|...
    ///
    /// Используем 2 прохода:
    /// 1. UnpackLow(Y,Cb) → Y0Cb0|Y1Cb1|Y2Cb2|Y3Cb3|Y4Cb4|Y5Cb5|Y6Cb6|Y7Cb7 (низ)
    /// 2. Shuffle с Cr для финального interleave
    /// </summary>
    public static Vector256<byte> YCbCrInterleaveMask0 { get; } = Vector256.Create(
        // Первые 24 байта из UnpackLow(Y,Cb) + Cr
        // YCb: Y0Cb0 Y1Cb1 Y2Cb2 Y3Cb3 | Y4Cb4 Y5Cb5 Y6Cb6 Y7Cb7
        // Нужно: Y0Cb0Cr0 Y1Cb1Cr1 Y2Cb2Cr2 Y3Cb3Cr3 Y4Cb4Cr4 Y5... (24 байта)
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 0x80, 0x80, 0x80, 0x80);

    public static Vector256<byte> CrInterleaveMask0 { get; } = Vector256.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region AVX2 Alpha

    /// <summary>Вектор 255 для альфа-канала (Vector256).</summary>
    public static Vector256<byte> Alpha255 { get; } = Vector256.Create((byte)255);

    #endregion
}

/// <summary>
/// AVX-512 векторные константы для Bgra32 (Vector512).
/// </summary>
internal static class Bgra32Avx512Vectors
{
    #region BGRA ↔ RGBA Swap Masks

    /// <summary>
    /// Swap R и B в 32-bit пикселях (AVX-512, 16 пикселей).
    /// </summary>
    public static Vector512<byte> SwapRB32Mask { get; } = Vector512.Create(
        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,  // lane 0
        2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,        // lane 1
        2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15,        // lane 2
        2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);       // lane 3

    #endregion

    #region Alpha Masks

    /// <summary>Маска для установки A=255 (16 пикселей).</summary>
    public static Vector512<byte> Alpha255Mask { get; } = Vector512.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion
}
