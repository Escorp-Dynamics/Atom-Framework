using System.Runtime.Intrinsics;

namespace Atom.Media.ColorSpaces;

/// <summary>
/// SSE4.1 векторные константы для Rgb24 (Vector128).
/// </summary>
internal static class Rgb24Sse41Vectors
{
    #region RGB24 Interleave Shuffle Masks

    /// <summary>
    /// InterleaveRgb8: RG → RGB24 (первые 16 байт).
    /// Комбинирует R и G каналы в RGB24 формат.
    /// </summary>
    public static Vector128<byte> RgToRgb24ShuffleMask0 { get; } = Vector128.Create(
        0, 1, 0x80, 2, 3, 0x80, 4, 5, 0x80, 6, 7, 0x80, 8, 9, 0x80, 10);

    /// <summary>
    /// InterleaveRgb8: B → RGB24 (первые 16 байт).
    /// Вставляет B канал в RGB24 формат.
    /// </summary>
    public static Vector128<byte> BToRgb24ShuffleMask0 { get; } = Vector128.Create(
        0x80, 0x80, 0, 0x80, 0x80, 1, 0x80, 0x80, 2, 0x80, 0x80, 3, 0x80, 0x80, 4, 0x80);

    /// <summary>
    /// InterleaveRgb8: RG → RGB24 (оставшиеся 8 байт).
    /// </summary>
    public static Vector128<byte> RgToRgb24ShuffleMask1 { get; } = Vector128.Create(
        11, 0x80, 12, 13, 0x80, 14, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    /// <summary>
    /// InterleaveRgb8: B → RGB24 (оставшиеся 8 байт).
    /// </summary>
    public static Vector128<byte> BToRgb24ShuffleMask1 { get; } = Vector128.Create(
        0x80, 5, 0x80, 0x80, 6, 0x80, 0x80, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

    #endregion
}