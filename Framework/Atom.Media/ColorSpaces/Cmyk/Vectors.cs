using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для CMYK (Vector128).
/// </summary>
internal static class CmykSse41Vectors
{
    #region Integer Constants

    /// <summary>Константа 255 int32.</summary>
    public static Vector128<int> C255I { get; } = Vector128.Create(255);

    /// <summary>Константа 1 int32.</summary>
    public static Vector128<int> C1I { get; } = Vector128.Create(1);

    /// <summary>Константа 257 int32 для 8-bit → 16-bit масштабирования.</summary>
    public static Vector128<int> Mult257I { get; } = Vector128.Create(257);

    #endregion

    #region Byte Constants

    /// <summary>Вектор 255 byte (все единицы).</summary>
    public static Vector128<byte> AllFF { get; } = Vector128.Create((byte)255);

    /// <summary>Константа 128 int32 для округления.</summary>
    public static Vector128<int> C128I { get; } = Vector128.Create(128);

    /// <summary>Вектор нулей int32.</summary>
    public static Vector128<int> ZeroI32 { get; } = Vector128<int>.Zero;

    #endregion

    #region Float Constants

    /// <summary>Вектор нулей float.</summary>
    public static Vector128<float> ZeroF { get; } = Vector128<float>.Zero;

    /// <summary>Вектор единиц float.</summary>
    public static Vector128<float> OneF { get; } = Vector128.Create(1f);

    /// <summary>Константа 2.0f для Newton-Raphson.</summary>
    public static Vector128<float> TwoF { get; } = Vector128.Create(2f);

    /// <summary>Константа 255f.</summary>
    public static Vector128<float> C255F { get; } = Vector128.Create(255f);

    /// <summary>Константа 1/255f.</summary>
    public static Vector128<float> Inv255F { get; } = Vector128.Create(1f / 255f);

    /// <summary>Константа 0.5f для округления.</summary>
    public static Vector128<float> HalfF { get; } = Vector128.Create(0.5f);

    /// <summary>Epsilon для предотвращения деления на ноль.</summary>
    public static Vector128<float> EpsilonF { get; } = Vector128.Create(1e-6f);

    /// <summary>Константа 128f.</summary>
    public static Vector128<float> C128F { get; } = Vector128.Create(128f);

    #endregion

    #region Shuffle Masks - Pack

    /// <summary>Упаковка int32 в bytes (младший байт каждого int32).</summary>
    public static Vector128<byte> PackInt32ToByte { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - RGBA32

    /// <summary>Извлечение R из RGBA32.</summary>
    public static Vector128<byte> ShuffleRgbaR { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGBA32.</summary>
    public static Vector128<byte> ShuffleRgbaG { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGBA32.</summary>
    public static Vector128<byte> ShuffleRgbaB { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - RGB24

    /// <summary>Извлечение R из RGB24 (позиции 0,3,6,9).</summary>
    public static Vector128<byte> ShuffleRgb24R { get; } = Vector128.Create(
        0, 3, 6, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGB24 (позиции 1,4,7,10).</summary>
    public static Vector128<byte> ShuffleRgb24G { get; } = Vector128.Create(
        1, 4, 7, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGB24 (позиции 2,5,8,11).</summary>
    public static Vector128<byte> ShuffleRgb24B { get; } = Vector128.Create(
        2, 5, 8, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24 R пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleRgb24R2 { get; } = Vector128.Create(
        4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24 G пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleRgb24G2 { get; } = Vector128.Create(
        5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGB24 B пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleRgb24B2 { get; } = Vector128.Create(
        6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - BGR24

    /// <summary>Извлечение R из BGR24 (позиции 2,5,8,11).</summary>
    public static Vector128<byte> ShuffleBgr24R { get; } = Vector128.Create(
        2, 5, 8, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из BGR24 (позиции 1,4,7,10).</summary>
    public static Vector128<byte> ShuffleBgr24G { get; } = Vector128.Create(
        1, 4, 7, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из BGR24 (позиции 0,3,6,9).</summary>
    public static Vector128<byte> ShuffleBgr24B { get; } = Vector128.Create(
        0, 3, 6, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGR24 R пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleBgr24R2 { get; } = Vector128.Create(
        6, 9, 12, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGR24 G пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleBgr24G2 { get; } = Vector128.Create(
        5, 8, 11, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>BGR24 B пиксели 4-7.</summary>
    public static Vector128<byte> ShuffleBgr24B2 { get; } = Vector128.Create(
        4, 7, 10, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - BGRA32

    /// <summary>Извлечение R из BGRA32.</summary>
    public static Vector128<byte> ShuffleBgra32R { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из BGRA32.</summary>
    public static Vector128<byte> ShuffleBgra32G { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из BGRA32.</summary>
    public static Vector128<byte> ShuffleBgra32B { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - CMYK

    /// <summary>Извлечение C из CMYK.</summary>
    public static Vector128<byte> ShuffleCmykC { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение M из CMYK.</summary>
    public static Vector128<byte> ShuffleCmykM { get; } = Vector128.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Y из CMYK.</summary>
    public static Vector128<byte> ShuffleCmykY { get; } = Vector128.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение K из CMYK.</summary>
    public static Vector128<byte> ShuffleCmykK { get; } = Vector128.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>RGBA32 → RGB24 shuffle.</summary>
    public static Vector128<byte> Rgba32ToRgb24Shuffle { get; } = Vector128.Create(
        0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - Gray16

    /// <summary>Извлечение старших байт из Gray16 (8 → 8 Gray8).</summary>
    public static Vector128<byte> ShuffleGray16HighByte { get; } = Vector128.Create(
        1, 3, 5, 7, 9, 11, 13, 15,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Gray8 K → CMYK: (0, 0, 0, K0), (0, 0, 0, K1), ...</summary>
    public static Vector128<byte> ShuffleGray8KToCmyk { get; } = Vector128.Create(
        0x80, 0x80, 0x80, 0,
        0x80, 0x80, 0x80, 1,
        0x80, 0x80, 0x80, 2,
        0x80, 0x80, 0x80, 3);

    #endregion

    #region YCbCr Coefficients (Float)

    /// <summary>1.402 для CrToR.</summary>
    public static Vector128<float> YCbCrCrToR { get; } = Vector128.Create(1.402f);

    /// <summary>-0.344136 для CbToG.</summary>
    public static Vector128<float> YCbCrCbToG { get; } = Vector128.Create(-0.344136f);

    /// <summary>-0.714136 для CrToG.</summary>
    public static Vector128<float> YCbCrCrToG { get; } = Vector128.Create(-0.714136f);

    /// <summary>1.772 для CbToB.</summary>
    public static Vector128<float> YCbCrCbToB { get; } = Vector128.Create(1.772f);

    /// <summary>0.299 для RtoY.</summary>
    public static Vector128<float> RgbToYR { get; } = Vector128.Create(0.299f);

    /// <summary>0.587 для GtoY.</summary>
    public static Vector128<float> RgbToYG { get; } = Vector128.Create(0.587f);

    /// <summary>0.114 для BtoY.</summary>
    public static Vector128<float> RgbToYB { get; } = Vector128.Create(0.114f);

    /// <summary>-0.168736 для RtoCb.</summary>
    public static Vector128<float> RgbToCbR { get; } = Vector128.Create(-0.168736f);

    /// <summary>-0.331264 для GtoCb.</summary>
    public static Vector128<float> RgbToCbG { get; } = Vector128.Create(-0.331264f);

    /// <summary>0.5 для BtoCb.</summary>
    public static Vector128<float> RgbToCbB { get; } = Vector128.Create(0.5f);

    /// <summary>0.5 для RtoCr.</summary>
    public static Vector128<float> RgbToCrR { get; } = Vector128.Create(0.5f);

    /// <summary>-0.418688 для GtoCr.</summary>
    public static Vector128<float> RgbToCrG { get; } = Vector128.Create(-0.418688f);

    /// <summary>-0.081312 для BtoCr.</summary>
    public static Vector128<float> RgbToCrB { get; } = Vector128.Create(-0.081312f);

    #endregion

    #region HSV Constants

    /// <summary>Константа 6f.</summary>
    public static Vector128<float> C6F { get; } = Vector128.Create(6f);

    /// <summary>Константа 60f.</summary>
    public static Vector128<float> C60F { get; } = Vector128.Create(60f);

    /// <summary>Константа 360f.</summary>
    public static Vector128<float> C360F { get; } = Vector128.Create(360f);

    /// <summary>Константа 1/6f.</summary>
    public static Vector128<float> Inv6F { get; } = Vector128.Create(1f / 6f);

    /// <summary>Константа 1/360f.</summary>
    public static Vector128<float> Inv360F { get; } = Vector128.Create(1f / 360f);

    /// <summary>Константа 43f.</summary>
    public static Vector128<float> C43F { get; } = Vector128.Create(43f);

    /// <summary>Константа 85f.</summary>
    public static Vector128<float> C85F { get; } = Vector128.Create(85f);

    /// <summary>Константа 171f.</summary>
    public static Vector128<float> C171F { get; } = Vector128.Create(171f);

    /// <summary>Константа 256f.</summary>
    public static Vector128<float> C256F { get; } = Vector128.Create(256f);

    /// <summary>Константа 2 int32.</summary>
    public static Vector128<int> TwoI { get; } = Vector128.Create(2);

    /// <summary>Константа 3 int32.</summary>
    public static Vector128<int> ThreeI { get; } = Vector128.Create(3);

    /// <summary>Константа 4 int32.</summary>
    public static Vector128<int> FourI { get; } = Vector128.Create(4);

    /// <summary>Константа 5 int32.</summary>
    public static Vector128<int> FiveI { get; } = Vector128.Create(5);

    /// <summary>Константа 6 int32.</summary>
    public static Vector128<int> SixI { get; } = Vector128.Create(6);

    /// <summary>Константа 63 int32.</summary>
    public static Vector128<int> C63I { get; } = Vector128.Create(63);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для CMYK (Vector256).
/// </summary>
internal static class CmykAvx2Vectors
{
    #region Integer Constants

    /// <summary>Константа 255 short.</summary>
    public static Vector256<short> C255S { get; } = Vector256.Create((short)255);

    /// <summary>Константа 255 int32.</summary>
    public static Vector256<int> C255I { get; } = Vector256.Create(255);

    /// <summary>Константа 1 int32.</summary>
    public static Vector256<int> C1I { get; } = Vector256.Create(1);

    /// <summary>Константа 257 int32 для 8-bit → 16-bit масштабирования.</summary>
    public static Vector256<int> Mult257I { get; } = Vector256.Create(257);

    /// <summary>Константа 128 int32 для округления.</summary>
    public static Vector256<int> C128I { get; } = Vector256.Create(128);

    /// <summary>Вектор нулей int32.</summary>
    public static Vector256<int> ZeroI32 { get; } = Vector256<int>.Zero;

    #endregion

    #region Float Constants

    /// <summary>Вектор нулей float.</summary>
    public static Vector256<float> ZeroF { get; } = Vector256<float>.Zero;

    /// <summary>Вектор единиц float.</summary>
    public static Vector256<float> OneF { get; } = Vector256.Create(1f);

    /// <summary>Константа 2.0f для Newton-Raphson.</summary>
    public static Vector256<float> TwoF { get; } = Vector256.Create(2f);

    /// <summary>Константа 255f.</summary>
    public static Vector256<float> C255F { get; } = Vector256.Create(255f);

    /// <summary>Константа 1/255f.</summary>
    public static Vector256<float> Inv255F { get; } = Vector256.Create(1f / 255f);

    /// <summary>Константа 0.5f для округления.</summary>
    public static Vector256<float> HalfF { get; } = Vector256.Create(0.5f);

    /// <summary>Epsilon для предотвращения деления на ноль.</summary>
    public static Vector256<float> EpsilonF { get; } = Vector256.Create(1e-6f);

    /// <summary>Константа 128f.</summary>
    public static Vector256<float> C128F { get; } = Vector256.Create(128f);

    #endregion

    #region Shuffle Masks - Pack

    /// <summary>Упаковка int32 в bytes (младший байт каждого int32).</summary>
    public static Vector256<byte> PackInt32ToByte { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - RGBA32 (to int32)

    /// <summary>Извлечение R из RGBA32 (zero-extend to int32).</summary>
    public static Vector256<byte> ShuffleRgbaR { get; } = Vector256.Create(
        0, 0x80, 0x80, 0x80, 4, 0x80, 0x80, 0x80, 8, 0x80, 0x80, 0x80, 12, 0x80, 0x80, 0x80,
        0, 0x80, 0x80, 0x80, 4, 0x80, 0x80, 0x80, 8, 0x80, 0x80, 0x80, 12, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGBA32 (zero-extend to int32).</summary>
    public static Vector256<byte> ShuffleRgbaG { get; } = Vector256.Create(
        1, 0x80, 0x80, 0x80, 5, 0x80, 0x80, 0x80, 9, 0x80, 0x80, 0x80, 13, 0x80, 0x80, 0x80,
        1, 0x80, 0x80, 0x80, 5, 0x80, 0x80, 0x80, 9, 0x80, 0x80, 0x80, 13, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGBA32 (zero-extend to int32).</summary>
    public static Vector256<byte> ShuffleRgbaB { get; } = Vector256.Create(
        2, 0x80, 0x80, 0x80, 6, 0x80, 0x80, 0x80, 10, 0x80, 0x80, 0x80, 14, 0x80, 0x80, 0x80,
        2, 0x80, 0x80, 0x80, 6, 0x80, 0x80, 0x80, 10, 0x80, 0x80, 0x80, 14, 0x80, 0x80, 0x80);

    #endregion

    #region Shuffle Masks - RGBA/CMYK Compact

    /// <summary>Компактное извлечение R из RGBA32.</summary>
    public static Vector256<byte> ShuffleRgbaCompactR { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение G из RGBA32.</summary>
    public static Vector256<byte> ShuffleRgbaCompactG { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение B из RGBA32.</summary>
    public static Vector256<byte> ShuffleRgbaCompactB { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение C из CMYK.</summary>
    public static Vector256<byte> ShuffleCmykCompactC { get; } = Vector256.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение M из CMYK.</summary>
    public static Vector256<byte> ShuffleCmykCompactM { get; } = Vector256.Create(
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 5, 9, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение Y из CMYK.</summary>
    public static Vector256<byte> ShuffleCmykCompactY { get; } = Vector256.Create(
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 6, 10, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Компактное извлечение K из CMYK.</summary>
    public static Vector256<byte> ShuffleCmykCompactK { get; } = Vector256.Create(
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        3, 7, 11, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region YCbCr Coefficients (Float)

    /// <summary>1.402 для CrToR.</summary>
    public static Vector256<float> YCbCrCrToR { get; } = Vector256.Create(1.402f);

    /// <summary>-0.344136 для CbToG.</summary>
    public static Vector256<float> YCbCrCbToG { get; } = Vector256.Create(-0.344136f);

    /// <summary>-0.714136 для CrToG.</summary>
    public static Vector256<float> YCbCrCrToG { get; } = Vector256.Create(-0.714136f);

    /// <summary>1.772 для CbToB.</summary>
    public static Vector256<float> YCbCrCbToB { get; } = Vector256.Create(1.772f);

    /// <summary>0.299 для RtoY.</summary>
    public static Vector256<float> RgbToYR { get; } = Vector256.Create(0.299f);

    /// <summary>0.587 для GtoY.</summary>
    public static Vector256<float> RgbToYG { get; } = Vector256.Create(0.587f);

    /// <summary>0.114 для BtoY.</summary>
    public static Vector256<float> RgbToYB { get; } = Vector256.Create(0.114f);

    /// <summary>-0.168736 для RtoCb.</summary>
    public static Vector256<float> RgbToCbR { get; } = Vector256.Create(-0.168736f);

    /// <summary>-0.331264 для GtoCb.</summary>
    public static Vector256<float> RgbToCbG { get; } = Vector256.Create(-0.331264f);

    /// <summary>0.5 для BtoCb.</summary>
    public static Vector256<float> RgbToCbB { get; } = Vector256.Create(0.5f);

    /// <summary>0.5 для RtoCr.</summary>
    public static Vector256<float> RgbToCrR { get; } = Vector256.Create(0.5f);

    /// <summary>-0.418688 для GtoCr.</summary>
    public static Vector256<float> RgbToCrG { get; } = Vector256.Create(-0.418688f);

    /// <summary>-0.081312 для BtoCr.</summary>
    public static Vector256<float> RgbToCrB { get; } = Vector256.Create(-0.081312f);

    #endregion

    #region HSV Constants

    /// <summary>Константа 6f.</summary>
    public static Vector256<float> C6F { get; } = Vector256.Create(6f);

    /// <summary>Константа 60f.</summary>
    public static Vector256<float> C60F { get; } = Vector256.Create(60f);

    /// <summary>Константа 360f.</summary>
    public static Vector256<float> C360F { get; } = Vector256.Create(360f);

    /// <summary>Константа 1/6f.</summary>
    public static Vector256<float> Inv6F { get; } = Vector256.Create(1f / 6f);

    /// <summary>Константа 1/360f.</summary>
    public static Vector256<float> Inv360F { get; } = Vector256.Create(1f / 360f);

    /// <summary>Константа 43f.</summary>
    public static Vector256<float> C43F { get; } = Vector256.Create(43f);

    /// <summary>Константа 85f.</summary>
    public static Vector256<float> C85F { get; } = Vector256.Create(85f);

    /// <summary>Константа 171f.</summary>
    public static Vector256<float> C171F { get; } = Vector256.Create(171f);

    /// <summary>Константа 256f.</summary>
    public static Vector256<float> C256F { get; } = Vector256.Create(256f);

    /// <summary>Вектор нулей int32.</summary>
    public static Vector256<int> ZeroI { get; } = Vector256<int>.Zero;

    /// <summary>Константа 1 int32.</summary>
    public static Vector256<int> OneI { get; } = Vector256.Create(1);

    /// <summary>Константа 2 int32.</summary>
    public static Vector256<int> TwoI { get; } = Vector256.Create(2);

    /// <summary>Константа 3 int32.</summary>
    public static Vector256<int> ThreeI { get; } = Vector256.Create(3);

    /// <summary>Константа 4 int32.</summary>
    public static Vector256<int> FourI { get; } = Vector256.Create(4);

    /// <summary>Константа 5 int32.</summary>
    public static Vector256<int> FiveI { get; } = Vector256.Create(5);

    /// <summary>Константа 6 int32.</summary>
    public static Vector256<int> SixI { get; } = Vector256.Create(6);

    /// <summary>Константа 63 int32.</summary>
    public static Vector256<int> C63I { get; } = Vector256.Create(63);

    #endregion
}
