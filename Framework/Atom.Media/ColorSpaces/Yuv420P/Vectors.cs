using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

// ═══════════════════════════════════════════════════════════════════════════════
// Lazy-кешированные SIMD векторы для Yuv420P конвертаций.
// ═══════════════════════════════════════════════════════════════════════════════
//
// BT.601 Full Range коэффициенты:
// Y  =  0.299 * R + 0.587 * G + 0.114 * B
// Cb = -0.169 * R - 0.331 * G + 0.500 * B + 128
// Cr =  0.500 * R - 0.419 * G - 0.081 * B + 128
//
// Обратные:
// R = Y                        + 1.402 * (Cr - 128)
// G = Y - 0.344 * (Cb - 128)   - 0.714 * (Cr - 128)
// B = Y + 1.772 * (Cb - 128)
//
// Q15 fixed-point (×32768) для MultiplyHighRoundScale:
// Y_R  = 0.299 × 32768 =  9798
// Y_G  = 0.587 × 32768 = 19235
// Y_B  = 0.114 × 32768 =  3736
// Cb_R = -0.169 × 32768 = -5538
// Cb_G = -0.331 × 32768 = -10846
// Cb_B = 0.500 × 32768 = 16384
// Cr_R = 0.500 × 32768 = 16384
// Cr_G = -0.419 × 32768 = -13730
// Cr_B = -0.081 × 32768 = -2654
//
// YUV → RGB (Q15):
// V_to_R = 1.402 × 32768 = 45941 (но > 32767, используем 22971 × 2 = (v*22971) >> 14)
// U_to_G = 0.344 × 32768 = 11277
// V_to_G = 0.714 × 32768 = 23401
// U_to_B = 1.772 × 32768 = 58065 (но > 32767, используем 29033 × 2 = (u*29033) >> 14)
//
// Q8 fixed-point (×256) для обратной конвертации:
// R = Y + ((359 * (V-128)) >> 8)
// G = Y - ((88 * (U-128) + 183 * (V-128)) >> 8)
// B = Y + ((454 * (U-128)) >> 8)
//
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// SSE4.1 (Vector128) векторы для Yuv420P конвертаций.
/// </summary>
internal static class Yuv420PSse41Vectors
{
    #region RGB → YUV (Q15 для MultiplyHighRoundScale)

    /// <summary>Q15: Y_R = 0.299 × 32768 ≈ 9798.</summary>
    public static Vector128<short> Q15_YR { get; } = Vector128.Create((short)9798);

    /// <summary>Q15: Y_G = 0.587 × 32768 ≈ 19235.</summary>
    public static Vector128<short> Q15_YG { get; } = Vector128.Create((short)19235);

    /// <summary>Q15: Y_B = 0.114 × 32768 ≈ 3736.</summary>
    public static Vector128<short> Q15_YB { get; } = Vector128.Create((short)3736);

    /// <summary>Q15: Cb_R = -0.169 × 32768 ≈ -5538.</summary>
    public static Vector128<short> Q15_CbR { get; } = Vector128.Create((short)-5538);

    /// <summary>Q15: Cb_G = -0.331 × 32768 ≈ -10846.</summary>
    public static Vector128<short> Q15_CbG { get; } = Vector128.Create((short)-10846);

    /// <summary>Q15: Cb_B = 0.500 × 32768 = 16384.</summary>
    public static Vector128<short> Q15_CbB { get; } = Vector128.Create((short)16384);

    /// <summary>Q15: Cr_R = 0.500 × 32768 = 16384.</summary>
    public static Vector128<short> Q15_CrR { get; } = Vector128.Create((short)16384);

    /// <summary>Q15: Cr_G = -0.419 × 32768 ≈ -13730.</summary>
    public static Vector128<short> Q15_CrG { get; } = Vector128.Create((short)-13730);

    /// <summary>Q15: Cr_B = -0.081 × 32768 ≈ -2654.</summary>
    public static Vector128<short> Q15_CrB { get; } = Vector128.Create((short)-2654);

    /// <summary>Смещение 128 для U/V.</summary>
    public static Vector128<short> Offset128 { get; } = Vector128.Create((short)128);

    #endregion

    #region YUV → RGB (Q8 коэффициенты)

    /// <summary>Q8: V→R = 1.402 × 256 ≈ 359.</summary>
    public static Vector128<short> Q8_VtoR { get; } = Vector128.Create((short)359);

    /// <summary>Q8: U→G = 0.344 × 256 ≈ 88.</summary>
    public static Vector128<short> Q8_UtoG { get; } = Vector128.Create((short)88);

    /// <summary>Q8: V→G = 0.714 × 256 ≈ 183.</summary>
    public static Vector128<short> Q8_VtoG { get; } = Vector128.Create((short)183);

    /// <summary>Q8: U→B = 1.772 × 256 ≈ 454.</summary>
    public static Vector128<short> Q8_UtoB { get; } = Vector128.Create((short)454);

    /// <summary>Rounding half: 128 для >> 8.</summary>
    public static Vector128<short> RoundHalf { get; } = Vector128.Create((short)128);

    #endregion

    #region Shuffle Masks — RGB24 деинтерливинг

    /// <summary>
    /// Shuffle маска для извлечения R из RGB24 (16 пикселей = 48 байт).
    /// Из байтов [0,3,6,9,12,15,...] в позиции [0-15].
    /// </summary>
    public static Vector128<byte> Rgb24ShuffleR0 { get; } = Vector128.Create(
        0, 3, 6, 9, 12, 15, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска R из второго блока (байты 16-31).</summary>
    public static Vector128<byte> Rgb24ShuffleR1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2, 5,
        8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска R из третьего блока (байты 32-47).</summary>
    public static Vector128<byte> Rgb24ShuffleR2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 1, 4, 7, 10, 13);

    /// <summary>Shuffle маска G из первого блока.</summary>
    public static Vector128<byte> Rgb24ShuffleG0 { get; } = Vector128.Create(
        1, 4, 7, 10, 13, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска G из второго блока.</summary>
    public static Vector128<byte> Rgb24ShuffleG1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0, 3, 6,
        9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска G из третьего блока.</summary>
    public static Vector128<byte> Rgb24ShuffleG2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 2, 5, 8, 11, 14);

    /// <summary>Shuffle маска B из первого блока.</summary>
    public static Vector128<byte> Rgb24ShuffleB0 { get; } = Vector128.Create(
        2, 5, 8, 11, 14, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска B из второго блока.</summary>
    public static Vector128<byte> Rgb24ShuffleB1 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 1, 4, 7,
        10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска B из третьего блока.</summary>
    public static Vector128<byte> Rgb24ShuffleB2 { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0, 3, 6, 9, 12, 15);

    #endregion

    #region Shuffle Masks — RGB24 интерливинг

    /// <summary>Shuffle маска для интерливинга R,G,B → RGB (первые 16 байт результата).</summary>
    public static Vector128<byte> InterleaveMask0 { get; } = Vector128.Create(
        (byte)0, 16, 32, 1, 17, 33, 2, 18, 34, 3, 19, 35, 4, 20, 36, 5);

    #endregion
}
