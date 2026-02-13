using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для YCbCr ↔ RGB24 конвертации (Vector128).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q16.
/// </summary>
internal static class YCbCrSse41Vectors
{
    #region YCbCr → RGB24 Constants (Q16)

    /// <summary>1.402 × 65536 = 91881</summary>
    public static Vector128<int> C1402 { get; } = Vector128.Create(91881);

    /// <summary>0.344136 × 65536 = 22554</summary>
    public static Vector128<int> C0344 { get; } = Vector128.Create(22554);

    /// <summary>0.714136 × 65536 = 46802</summary>
    public static Vector128<int> C0714 { get; } = Vector128.Create(46802);

    /// <summary>1.772 × 65536 = 116130</summary>
    public static Vector128<int> C1772 { get; } = Vector128.Create(116130);

    #endregion

    #region RGB24 → YCbCr Constants (Q16)

    /// <summary>0.299 × 65536 = 19595</summary>
    public static Vector128<int> CYR { get; } = Vector128.Create(19595);

    /// <summary>0.587 × 65536 = 38470</summary>
    public static Vector128<int> CYG { get; } = Vector128.Create(38470);

    /// <summary>0.114 × 65536 = 7471</summary>
    public static Vector128<int> CYB { get; } = Vector128.Create(7471);

    /// <summary>-0.168736 × 65536 = -11056</summary>
    public static Vector128<int> CCbR { get; } = Vector128.Create(-11056);

    /// <summary>-0.331264 × 65536 = -21712</summary>
    public static Vector128<int> CCbG { get; } = Vector128.Create(-21712);

    /// <summary>0.5 × 65536 = 32768</summary>
    public static Vector128<int> CCbB { get; } = Vector128.Create(32768);

    /// <summary>0.5 × 65536 = 32768</summary>
    public static Vector128<int> CCrR { get; } = Vector128.Create(32768);

    /// <summary>-0.418688 × 65536 = -27440</summary>
    public static Vector128<int> CCrG { get; } = Vector128.Create(-27440);

    /// <summary>-0.081312 × 65536 = -5328</summary>
    public static Vector128<int> CCrB { get; } = Vector128.Create(-5328);

    #endregion

    #region Common Constants

    /// <summary>128 (смещение для Cb/Cr)</summary>
    public static Vector128<int> C128 { get; } = Vector128.Create(128);

    /// <summary>0.5 × 65536 = 32768 (для округления)</summary>
    public static Vector128<int> Half { get; } = Vector128.Create(32768);

    #endregion

    #region Deinterleave Shuffle Masks (YCbCr → Y, Cb, Cr)

    // Деинтерлив YCbCr (24 байта = 8 триплетов Y0Cb0Cr0 Y1Cb1Cr1 ...)
    // bytes0 = первые 16 байт, bytes1 = байты 16-23 (в нижней половине Vector128)

    /// <summary>Извлечение Y из bytes0: берём байты 0, 3, 6, 9, 12, 15, остальные 0x80 (ноль)</summary>
    public static Vector128<byte> ShuffleY0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Y из bytes1: берём байты 2, 5 (позиции 18, 21 в исходных данных)</summary>
    public static Vector128<byte> ShuffleY1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Cb из bytes0: берём байты 1, 4, 7, 10, 13</summary>
    public static Vector128<byte> ShuffleCb0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Cb из bytes1: берём байты 0, 3, 6 (позиции 16, 19, 22)</summary>
    public static Vector128<byte> ShuffleCb1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Cr из bytes0: берём байты 2, 5, 8, 11, 14</summary>
    public static Vector128<byte> ShuffleCr0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Cr из bytes1: берём байты 1, 4, 7 (позиции 17, 20, 23)</summary>
    public static Vector128<byte> ShuffleCr1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Deinterleave Shuffle Masks (RGB24 → R, G, B)

    // Деинтерлив RGB24 (24 байта = 8 триплетов R0G0B0 R1G1B1 ...)
    // Аналогично YCbCr: R=Y, G=Cb, B=Cr по позициям

    /// <summary>Извлечение R из bytes0: берём байты 0, 3, 6, 9, 12, 15</summary>
    public static Vector128<byte> ShuffleR0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из bytes1: берём байты 2, 5 (позиции 18, 21)</summary>
    public static Vector128<byte> ShuffleR1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes0: берём байты 1, 4, 7, 10, 13</summary>
    public static Vector128<byte> ShuffleG0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из bytes1: берём байты 0, 3, 6 (позиции 16, 19, 22)</summary>
    public static Vector128<byte> ShuffleG1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из bytes0: берём байты 2, 5, 8, 11, 14</summary>
    public static Vector128<byte> ShuffleB0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из bytes1: берём байты 1, 4, 7 (позиции 17, 20, 23)</summary>
    public static Vector128<byte> ShuffleB1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Gray16/Gray8 ↔ YCbCr Constants

    /// <summary>Множитель 257 для конвертации Y (0-255) → Gray16 (0-65535).</summary>
    public static Vector128<short> Mult257 { get; } = Vector128.Create((short)257);

    /// <summary>Shuffle маска для извлечения старших байтов из Gray16 (байты 1,3,5,7,9,11,13,15).</summary>
    public static Vector128<byte> ShuffleHighBytes { get; } = Vector128.Create(
        1, 3, 5, 7, 9, 11, 13, 15,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80).AsByte();

    /// <summary>Маска для Cb/Cr значений 128 в позициях 1,2,4,5,7,8,10,11,13,14 (паттерн YCbCr).</summary>
    public static Vector128<byte> CbCrMask128 { get; } = Vector128.Create(
        0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0).AsByte();

    /// <summary>Маска для Cb/Cr значений 128, паттерн 2 (сдвинутый).</summary>
    public static Vector128<byte> CbCrMask128_2 { get; } = Vector128.Create(
        128, 128, 0, 128, 128, 0, 128, 128, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80).AsByte();

    #endregion

    #region Interleave Shuffle Masks (Y, Cb, Cr → YCbCr)

    /// <summary>InterleaveYCbCr8: YCb → YCbCr (первые 16 байт).</summary>
    public static Vector128<byte> YCbToYCbCrShuffleMask0 { get; } = Vector128.Create(
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 8, 9, 0x80, 10).AsByte();

    /// <summary>InterleaveYCbCr8: Cr → YCbCr (первые 16 байт).</summary>
    public static Vector128<byte> CrToYCbCrShuffleMask0 { get; } = Vector128.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80).AsByte();

    /// <summary>InterleaveYCbCr8: YCb → YCbCr (оставшиеся 8 байт).</summary>
    public static Vector128<byte> YCbToYCbCrShuffleMask1 { get; } = Vector128.Create(
        11, 0x80, 12, 13, 0x80, 14, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80).AsByte();

    /// <summary>InterleaveYCbCr8: Cr → YCbCr (оставшиеся 8 байт).</summary>
    public static Vector128<byte> CrToYCbCrShuffleMask1 { get; } = Vector128.Create(
        0x80, 5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80).AsByte();

    /// <summary>Маска для Cb/Cr значений 128, паттерн 3 (для склейки средней части).</summary>
    public static Vector128<byte> CbCrMask128_3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0, 128, 128, 0, 128, 128, 0, 128).AsByte();

    /// <summary>Маска для Cb/Cr значений 128, паттерн 4 (финальный блок).</summary>
    public static Vector128<byte> CbCrMask128_4 { get; } = Vector128.Create(
        128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128).AsByte();

    /// <summary>Маска для Y значений (0xFF в позициях 0,3,6,9,12,15).</summary>
    public static Vector128<byte> YMask128 { get; } = Vector128.Create(
        0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF).AsByte();

    /// <summary>Маска для Y значений, паттерн 2 (позиции 2,5).</summary>
    public static Vector128<byte> YMask128_2 { get; } = Vector128.Create(
        0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80).AsByte();

    /// <summary>Маска для Y значений, паттерн 3 (для склейки средней части).</summary>
    public static Vector128<byte> YMask128_3 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0).AsByte();

    /// <summary>Маска для Y значений, паттерн 4 (финальный блок).</summary>
    public static Vector128<byte> YMask128_4 { get; } = Vector128.Create(
        0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0).AsByte();

    /// <summary>Маска для Cb/Cr значений 128, альтернативный полный 16-байтный паттерн (сдвинутый).</summary>
    public static Vector128<byte> CbCrMask128_Alt { get; } = Vector128.Create(
        128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128, 128, 0, 128).AsByte();

    /// <summary>Маска для Y значений, альтернативный полный 16-байтный паттерн (позиции 2,5,8,11,14).</summary>
    public static Vector128<byte> YMask128_Alt { get; } = Vector128.Create(
        0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0, 0, 0xFF, 0).AsByte();

    #endregion
}

/// <summary>
/// AVX2 векторные константы для YCbCr ↔ RGB24 конвертации (Vector256).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q16 (int32).
/// </summary>
internal static class YCbCrAvx2Vectors
{
    #region YCbCr → RGB24 Constants (Q16, int32)

    /// <summary>1.402 × 65536 = 91881</summary>
    public static Vector256<int> C1402 { get; } = Vector256.Create(91881);

    /// <summary>0.344136 × 65536 = 22554</summary>
    public static Vector256<int> C0344 { get; } = Vector256.Create(22554);

    /// <summary>0.714136 × 65536 = 46802</summary>
    public static Vector256<int> C0714 { get; } = Vector256.Create(46802);

    /// <summary>1.772 × 65536 = 116130</summary>
    public static Vector256<int> C1772 { get; } = Vector256.Create(116130);

    #endregion

    #region RGB24 → YCbCr Constants (Q16, int32)

    /// <summary>0.299 × 65536 = 19595</summary>
    public static Vector256<int> CYR { get; } = Vector256.Create(19595);

    /// <summary>0.587 × 65536 = 38470</summary>
    public static Vector256<int> CYG { get; } = Vector256.Create(38470);

    /// <summary>0.114 × 65536 = 7471</summary>
    public static Vector256<int> CYB { get; } = Vector256.Create(7471);

    /// <summary>-0.168736 × 65536 = -11056</summary>
    public static Vector256<int> CCbR { get; } = Vector256.Create(-11056);

    /// <summary>-0.331264 × 65536 = -21712</summary>
    public static Vector256<int> CCbG { get; } = Vector256.Create(-21712);

    /// <summary>0.5 × 65536 = 32768</summary>
    public static Vector256<int> CCbB { get; } = Vector256.Create(32768);

    /// <summary>0.5 × 65536 = 32768</summary>
    public static Vector256<int> CCrR { get; } = Vector256.Create(32768);

    /// <summary>-0.418688 × 65536 = -27440</summary>
    public static Vector256<int> CCrG { get; } = Vector256.Create(-27440);

    /// <summary>-0.081312 × 65536 = -5328</summary>
    public static Vector256<int> CCrB { get; } = Vector256.Create(-5328);

    #endregion

    #region Common Constants

    /// <summary>128 (смещение для Cb/Cr)</summary>
    public static Vector256<int> C128 { get; } = Vector256.Create(128);

    /// <summary>0.5 × 65536 = 32768 (для округления)</summary>
    public static Vector256<int> Half { get; } = Vector256.Create(32768);

    #endregion
}

/// <summary>
/// AVX-512 векторные константы для YCbCr ↔ RGB24 конвертации (Vector512&lt;short&gt;).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q15 (×32768).
/// Обрабатывает 32 пикселя за итерацию.
/// Коэффициенты > 1.0 разделены: 1.402 = 1 + 0.402, 1.772 = 1 + 0.772.
/// </summary>
internal static class YCbCrAvx512Vectors
{
    #region YCbCr → RGB24 Constants (Q15)

    /// <summary>0.402 × 32768 = 13172 (для R = Y + Cr + 0.402×Cr)</summary>
    public static Vector512<short> C0402 { get; } = Vector512.Create((short)13172);

    /// <summary>0.344136 × 32768 = 11277</summary>
    public static Vector512<short> C0344 { get; } = Vector512.Create((short)11277);

    /// <summary>0.714136 × 32768 = 23401</summary>
    public static Vector512<short> C0714 { get; } = Vector512.Create((short)23401);

    /// <summary>0.772 × 32768 = 25297 (для B = Y + Cb + 0.772×Cb)</summary>
    public static Vector512<short> C0772 { get; } = Vector512.Create((short)25297);

    #endregion

    #region RGB24 → YCbCr Constants (Q15)

    /// <summary>0.299 × 32768 = 9798</summary>
    public static Vector512<short> CYR { get; } = Vector512.Create((short)9798);

    /// <summary>0.587 × 32768 = 19235</summary>
    public static Vector512<short> CYG { get; } = Vector512.Create((short)19235);

    /// <summary>0.114 × 32768 = 3736</summary>
    public static Vector512<short> CYB { get; } = Vector512.Create((short)3736);

    /// <summary>-0.168736 × 32768 = -5528</summary>
    public static Vector512<short> CCbR { get; } = Vector512.Create((short)-5528);

    /// <summary>-0.331264 × 32768 = -10856</summary>
    public static Vector512<short> CCbG { get; } = Vector512.Create((short)-10856);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector512<short> CCbB { get; } = Vector512.Create((short)16384);

    /// <summary>0.5 × 32768 = 16384</summary>
    public static Vector512<short> CCrR { get; } = Vector512.Create((short)16384);

    /// <summary>-0.418688 × 32768 = -13720</summary>
    public static Vector512<short> CCrG { get; } = Vector512.Create((short)-13720);

    /// <summary>-0.081312 × 32768 = -2664</summary>
    public static Vector512<short> CCrB { get; } = Vector512.Create((short)-2664);

    #endregion

    #region Common Constants

    /// <summary>128 (смещение для Cb/Cr)</summary>
    public static Vector512<short> C128 { get; } = Vector512.Create((short)128);

    #endregion
}
