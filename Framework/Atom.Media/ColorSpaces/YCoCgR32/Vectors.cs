using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для YCoCgR32 конвертаций (Vector128).
/// Класс загружается только при использовании SSE4.1 ускорителя.
/// </summary>
internal static class YCoCgR32Sse41Vectors
{
    #region Arithmetic Constants

    /// <summary>Константа 255 для сдвига Co/Cg в положительный диапазон.</summary>
    public static Vector128<short> Offset255 { get; } = Vector128.Create((short)255);

    /// <summary>Константа 1 для извлечения LSB.</summary>
    public static Vector128<short> One { get; } = Vector128.Create((short)1);

    /// <summary>Константа 255 для маскирования байт.</summary>
    public static Vector128<short> Mask255 { get; } = Vector128.Create((short)255);

    /// <summary>Альфа-канал 255 для RGBA результата.</summary>
    public static Vector128<byte> Alpha255 { get; } = Vector128.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion

    #region RGBA32 Shuffle Masks (Deinterleave R, G, B from RGBA)

    /// <summary>Извлечение R из RGBA: R0 _ R1 _ R2 _ R3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleRgbaToR { get; } = Vector128.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGBA: G0 _ G1 _ G2 _ G3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleRgbaToG { get; } = Vector128.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGBA: B0 _ B1 _ B2 _ B3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleRgbaToB { get; } = Vector128.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region BGRA32 Shuffle Masks (Deinterleave B, G, R from BGRA)

    /// <summary>Извлечение B из BGRA: B0 _ B1 _ B2 _ B3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleBgraToB { get; } = Vector128.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из BGRA: G0 _ G1 _ G2 _ G3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleBgraToG { get; } = Vector128.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из BGRA: R0 _ R1 _ R2 _ R3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleBgraToR { get; } = Vector128.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region YCoCgR32 Shuffle Masks (Deinterleave Y, CoHigh, CgHigh, Frac)

    /// <summary>Извлечение Y из YCoCgR32: Y0 _ Y1 _ Y2 _ Y3 _ (zero-extended to short).</summary>
    public static Vector128<byte> ShuffleYCoCgToY { get; } = Vector128.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение CoHigh из YCoCgR32.</summary>
    public static Vector128<byte> ShuffleYCoCgToCoH { get; } = Vector128.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение CgHigh из YCoCgR32.</summary>
    public static Vector128<byte> ShuffleYCoCgToCgH { get; } = Vector128.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Frac из YCoCgR32.</summary>
    public static Vector128<byte> ShuffleYCoCgToFrac { get; } = Vector128.Create(
        3, 0x80, 7, 0x80, 11, 0x80, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region RGB24 Shuffle Masks (Deinterleave R, G, B from 12 bytes = 4 pixels)

    /// <summary>Извлечение R из RGB24 (4 пикселя = 12 байт): R0 _ R1 _ R2 _ R3 _.</summary>
    public static Vector128<byte> ShuffleRgb24ToR { get; } = Vector128.Create(
        0, 0x80, 3, 0x80, 6, 0x80, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGB24 (4 пикселя = 12 байт): G0 _ G1 _ G2 _ G3 _.</summary>
    public static Vector128<byte> ShuffleRgb24ToG { get; } = Vector128.Create(
        1, 0x80, 4, 0x80, 7, 0x80, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGB24 (4 пикселя = 12 байт): B0 _ B1 _ B2 _ B3 _.</summary>
    public static Vector128<byte> ShuffleRgb24ToB { get; } = Vector128.Create(
        2, 0x80, 5, 0x80, 8, 0x80, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region BGR24 Shuffle Masks (Deinterleave B, G, R from 12 bytes = 4 pixels)

    /// <summary>Извлечение B из BGR24 (4 пикселя = 12 байт): B0 _ B1 _ B2 _ B3 _.</summary>
    public static Vector128<byte> ShuffleBgr24ToB { get; } = Vector128.Create(
        0, 0x80, 3, 0x80, 6, 0x80, 9, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из BGR24 (4 пикселя = 12 байт): G0 _ G1 _ G2 _ G3 _.</summary>
    public static Vector128<byte> ShuffleBgr24ToG { get; } = Vector128.Create(
        1, 0x80, 4, 0x80, 7, 0x80, 10, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение R из BGR24 (4 пикселя = 12 байт): R0 _ R1 _ R2 _ R3 _.</summary>
    public static Vector128<byte> ShuffleBgr24ToR { get; } = Vector128.Create(
        2, 0x80, 5, 0x80, 8, 0x80, 11, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Gray8 Constants

    /// <summary>Значение CoHigh для нулевого Co (127).</summary>
    public static Vector128<byte> ZeroCoHigh { get; } = Vector128.Create((byte)127);

    /// <summary>Значение CgHigh для нулевого Cg (127).</summary>
    public static Vector128<byte> ZeroCgHigh { get; } = Vector128.Create((byte)127);

    /// <summary>Значение Frac для нулевых Co и Cg (0b11 = 3).</summary>
    public static Vector128<byte> ZeroFrac { get; } = Vector128.Create((byte)3);

    #endregion

    #region Gray16 Constants

    /// <summary>Множитель 255 для деления 16-bit → 8-bit (Q16).</summary>
    public static Vector128<int> Mult255 { get; } = Vector128.Create(255);

    /// <summary>Константа 32768 для округления Q16.</summary>
    public static Vector128<int> Round32768 { get; } = Vector128.Create(32768);

    /// <summary>Множитель 257 для 8-bit → 16-bit (Y * 257).</summary>
    public static Vector128<int> Mult257 { get; } = Vector128.Create(257);

    /// <summary>Множитель 255 для ushort формата.</summary>
    public static Vector128<ushort> Scale255UShort { get; } = Vector128.Create((ushort)255);

    /// <summary>Shuffle маска для извлечения Y из YCoCgR32 (compact, 4 bytes).</summary>
    public static Vector128<byte> ShuffleYCoCgToYCompact { get; } = Vector128.Create(
        0, 4, 8, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region RGB24/BGR24 Output Shuffle Masks

    /// <summary>Упаковка R0 G0 B0 R1 G1 B1 R2 G2 B2 R3 G3 B3 из interleaved short values.</summary>
    public static Vector128<byte> ShuffleRgb24Out { get; } = Vector128.Create(
        0, 4, 8, 1, 5, 9, 2, 6, 10, 3, 7, 11, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Упаковка B0 G0 R0 B1 G1 R1 B2 G2 R2 B3 G3 R3 из interleaved short values.</summary>
    public static Vector128<byte> ShuffleBgr24Out { get; } = Vector128.Create(
        8, 4, 0, 9, 5, 1, 10, 6, 2, 11, 7, 3, 0x80, 0x80, 0x80, 0x80);

    #endregion
}

/// <summary>
/// AVX2 векторные константы для YCoCgR32 конвертаций (Vector256).
/// Класс загружается только при использовании AVX2 ускорителя.
/// </summary>
internal static class YCoCgR32Avx2Vectors
{
    #region Arithmetic Constants

    /// <summary>Константа 255 для сдвига Co/Cg в положительный диапазон (short).</summary>
    public static Vector256<short> Offset255 { get; } = Vector256.Create((short)255);

    /// <summary>Константа 1 для извлечения LSB (short).</summary>
    public static Vector256<short> One { get; } = Vector256.Create((short)1);

    /// <summary>Константа 255 для маскирования (short).</summary>
    public static Vector256<short> Mask255 { get; } = Vector256.Create((short)255);

    /// <summary>Альфа-канал 255 для RGBA результата (8 пикселей).</summary>
    public static Vector256<byte> Alpha255 { get; } = Vector256.Create(
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

    #endregion

    #region RGBA32/BGRA32 Shuffle Masks (AVX2 in-lane)

    /// <summary>Извлечение R из RGBA (in-lane, 4 пикселя per lane).</summary>
    public static Vector256<byte> ShuffleRgbaToR { get; } = Vector256.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение G из RGBA (in-lane).</summary>
    public static Vector256<byte> ShuffleRgbaToG { get; } = Vector256.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение B из RGBA (in-lane).</summary>
    public static Vector256<byte> ShuffleRgbaToB { get; } = Vector256.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region YCoCgR32 Shuffle Masks (in-lane)

    /// <summary>Извлечение Y из YCoCgR32 (in-lane).</summary>
    public static Vector256<byte> ShuffleYCoCgToY { get; } = Vector256.Create(
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0, 0x80, 4, 0x80, 8, 0x80, 12, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение CoHigh из YCoCgR32 (in-lane).</summary>
    public static Vector256<byte> ShuffleYCoCgToCoH { get; } = Vector256.Create(
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        1, 0x80, 5, 0x80, 9, 0x80, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение CgHigh из YCoCgR32 (in-lane).</summary>
    public static Vector256<byte> ShuffleYCoCgToCgH { get; } = Vector256.Create(
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        2, 0x80, 6, 0x80, 10, 0x80, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>Извлечение Frac из YCoCgR32 (in-lane).</summary>
    public static Vector256<byte> ShuffleYCoCgToFrac { get; } = Vector256.Create(
        3, 0x80, 7, 0x80, 11, 0x80, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        3, 0x80, 7, 0x80, 11, 0x80, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion

    #region Gray Constants

    /// <summary>Значение CoHigh для нулевого Co (127).</summary>
    public static Vector256<byte> ZeroCoHigh { get; } = Vector256.Create((byte)127);

    /// <summary>Значение CgHigh для нулевого Cg (127).</summary>
    public static Vector256<byte> ZeroCgHigh { get; } = Vector256.Create((byte)127);

    /// <summary>Значение Frac для нулевых Co и Cg (3).</summary>
    public static Vector256<byte> ZeroFrac { get; } = Vector256.Create((byte)3);

    /// <summary>Множитель 255 для деления 16-bit → 8-bit (Q16).</summary>
    public static Vector256<int> Mult255 { get; } = Vector256.Create(255);

    /// <summary>Константа 32768 для округления Q16.</summary>
    public static Vector256<int> Round32768 { get; } = Vector256.Create(32768);

    /// <summary>Множитель 257 для 8-bit → 16-bit.</summary>
    public static Vector256<int> Mult257 { get; } = Vector256.Create(257);

    #endregion
}
