using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для Gray16 (Vector128).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q16.
/// </summary>
internal static class Gray16Sse41Vectors
{
    #region Q16 Coefficients (×65536)

    /// <summary>0.299 × 65536 = 19595</summary>
    public static Vector128<int> CoefficientR { get; } = Vector128.Create(19595);

    /// <summary>0.587 × 65536 = 38470</summary>
    public static Vector128<int> CoefficientG { get; } = Vector128.Create(38470);

    /// <summary>0.114 × 65536 = 7471</summary>
    public static Vector128<int> CoefficientB { get; } = Vector128.Create(7471);

    /// <summary>Половина для округления Q16 (32768).</summary>
    public static Vector128<int> Half { get; } = Vector128.Create(32768);

    /// <summary>Множитель 257 для 8-bit → 16-bit.</summary>
    public static Vector128<int> Scale8To16 { get; } = Vector128.Create(257);

    #endregion

    #region Shuffle Masks - RGBA/BGRA (деинтерливинг)

    /// <summary>Деинтерливинг R из RGBA: R0,R1,R2,R3 (байты 0,4,8,12).</summary>
    public static Vector128<byte> ShuffleRgbaToR { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг G из RGBA: G0,G1,G2,G3 (байты 1,5,9,13).</summary>
    public static Vector128<byte> ShuffleRgbaToG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг B из RGBA: B0,B1,B2,B3 (байты 2,6,10,14).</summary>
    public static Vector128<byte> ShuffleRgbaToB { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

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

    #region CMYK Shuffle Masks

    /// <summary>Деинтерливинг C из CMYK: C0,C1,C2,C3 (байты 0,4,8,12).</summary>
    public static Vector128<byte> ShuffleCmykToC { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг M из CMYK: M0,M1,M2,M3 (байты 1,5,9,13).</summary>
    public static Vector128<byte> ShuffleCmykToM { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг Y из CMYK: Y0,Y1,Y2,Y3 (байты 2,6,10,14).</summary>
    public static Vector128<byte> ShuffleCmykToY { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Деинтерливинг K из CMYK: K0,K1,K2,K3 (байты 3,7,11,15).</summary>
    public static Vector128<byte> ShuffleCmykToK { get; } = Vector128.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Byte Constants

    /// <summary>Вектор 255 byte (все единицы).</summary>
    public static Vector128<byte> AllFF { get; } = Vector128.Create((byte)255);

    /// <summary>Shuffle маска для Gray8 → CMYK (0, 0, 0, K).</summary>
    public static Vector128<byte> ShuffleGray8ToCmyk { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0,
        0x80, 0x80, 0x80, 1,
        0x80, 0x80, 0x80, 2,
        0x80, 0x80, 0x80, 3);

    #endregion

    #region Float Constants

    /// <summary>1.0f × 4.</summary>
    public static Vector128<float> OneF { get; } = Vector128.Create(1.0f);

    /// <summary>255.0f × 4.</summary>
    public static Vector128<float> C255F { get; } = Vector128.Create(255.0f);

    /// <summary>1.0f / 255.0f × 4.</summary>
    public static Vector128<float> Inv255F { get; } = Vector128.Create(1.0f / 255.0f);

    /// <summary>0.5f × 4.</summary>
    public static Vector128<float> HalfF { get; } = Vector128.Create(0.5f);

    #endregion

    #region HSV/YCoCgR Constants

    /// <summary>255 ushort для HSV масштабирования.</summary>
    public static Vector128<ushort> Mult255 { get; } = Vector128.Create((ushort)255);

    /// <summary>Shuffle для V извлечения из HSV.</summary>
    public static Vector128<byte> ShuffleHsvToV { get; } = Vector128.Create(
        2, 0x80, 5, 0x80, 8, 0x80, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Shuffle маска для Gray16 → HSV (H=0, S=0, V=gray).</summary>
    public static Vector128<byte> ShuffleGrayToHsv { get; } = Vector128.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Множитель 257 short для 8-bit → 16-bit.</summary>
    public static Vector128<short> Mult257 { get; } = Vector128.Create((short)257);

    /// <summary>127 byte для YCoCgR neutral chroma.</summary>
    public static Vector128<byte> Neutral127 { get; } = Vector128.Create((byte)127);

    /// <summary>3 byte для YCoCgR fractional part.</summary>
    public static Vector128<byte> Neutral3 { get; } = Vector128.Create((byte)3);

    /// <summary>255 ushort для YCoCgR масштабирования.</summary>
    public static Vector128<ushort> Scale255 { get; } = Vector128.Create((ushort)255);

    /// <summary>Shuffle Y из YCoCgR32 (4-byte stride).</summary>
    public static Vector128<byte> ShuffleYCoCgToY { get; } = Vector128.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Gray16 → RGB24/BGR24 Shuffle Masks

    /// <summary>Shuffle для Gray16 → RGB24/BGR24: извлекает старшие байты ushort и дублирует для первых 16 выходных байт.</summary>
    public static Vector128<byte> ShuffleGray16ToRgb24Hi { get; } = Vector128.Create(
        (byte)1, 1, 1, 3, 3, 3, 5, 5, 5, 7, 7, 7, 9, 9, 9, 11);

    /// <summary>Shuffle для Gray16 → RGB24/BGR24: оставшиеся 8 выходных байт (overlapping).</summary>
    public static Vector128<byte> ShuffleGray16ToRgb24Hi2 { get; } = Vector128.Create(
        11, 11, 13, 13, 13, 15, 15, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для Gray16 (Vector256).
/// ITU-R BT.601 коэффициенты в Fixed-Point Q15/Q16.
/// </summary>
internal static class Gray16Avx2Vectors
{
    #region Q15 Coefficients (×32768) for int16 math

    /// <summary>0.299 × 32768 = 9798</summary>
    public static Vector256<short> CoefficientR_Q15 { get; } = Vector256.Create((short)9798);

    /// <summary>0.587 × 32768 = 19235</summary>
    public static Vector256<short> CoefficientG_Q15 { get; } = Vector256.Create((short)19235);

    /// <summary>0.114 × 32768 = 3735</summary>
    public static Vector256<short> CoefficientB_Q15 { get; } = Vector256.Create((short)3735);

    #endregion

    #region Q16 Coefficients (×65536) for int32 math

    /// <summary>0.299 × 65536 = 19595</summary>
    public static Vector256<int> CoefficientR { get; } = Vector256.Create(19595);

    /// <summary>0.587 × 65536 = 38470</summary>
    public static Vector256<int> CoefficientG { get; } = Vector256.Create(38470);

    /// <summary>0.114 × 65536 = 7471</summary>
    public static Vector256<int> CoefficientB { get; } = Vector256.Create(7471);

    /// <summary>Половина для округления Q16 (32768).</summary>
    public static Vector256<int> Half { get; } = Vector256.Create(32768);

    /// <summary>Множитель 257 для 8-bit → 16-bit.</summary>
    public static Vector256<int> Scale8To16 { get; } = Vector256.Create(257);

    /// <summary>Множитель 257 short.</summary>
    public static Vector256<short> Scale8To16Short { get; } = Vector256.Create((short)257);

    #endregion

    #region Float Constants

    /// <summary>1.0f × 8.</summary>
    public static Vector256<float> OneF { get; } = Vector256.Create(1.0f);

    /// <summary>255.0f × 8.</summary>
    public static Vector256<float> C255F { get; } = Vector256.Create(255.0f);

    /// <summary>1.0f / 255.0f × 8.</summary>
    public static Vector256<float> Inv255F { get; } = Vector256.Create(1.0f / 255.0f);

    /// <summary>0.5f × 8.</summary>
    public static Vector256<float> HalfF { get; } = Vector256.Create(0.5f);

    /// <summary>BT.601 R коэффициент 0.299f × 8.</summary>
    public static Vector256<float> CoefR { get; } = Vector256.Create(0.299f);

    /// <summary>BT.601 G коэффициент 0.587f × 8.</summary>
    public static Vector256<float> CoefG { get; } = Vector256.Create(0.587f);

    /// <summary>BT.601 B коэффициент 0.114f × 8.</summary>
    public static Vector256<float> CoefB { get; } = Vector256.Create(0.114f);

    #endregion

    #region HSV Constants

    /// <summary>255 ushort × 16 для HSV масштабирования.</summary>
    public static Vector256<ushort> Mult255 { get; } = Vector256.Create((ushort)255);

    #endregion
}
