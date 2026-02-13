#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Цветовое пространство видео.
/// </summary>
public enum ColorSpace : byte
{
    /// <summary>Неопределённое цветовое пространство.</summary>
    Unknown = 0,

    /// <summary>sRGB — стандартное RGB для мониторов.</summary>
    Srgb = 1,

    /// <summary>Linear RGB (без гамма-коррекции).</summary>
    LinearRgb = 2,

    /// <summary>BT.601 (SD видео, NTSC/PAL).</summary>
    Bt601 = 10,

    /// <summary>BT.709 (HD видео, 1080p).</summary>
    Bt709 = 11,

    /// <summary>BT.2020 (4K/8K, HDR, Wide Color Gamut).</summary>
    Bt2020 = 12,

    /// <summary>BT.2100 PQ (HDR10, Dolby Vision).</summary>
    Bt2100Pq = 13,

    /// <summary>BT.2100 HLG (BBC/NHK HDR).</summary>
    Bt2100Hlg = 14,
}

/// <summary>
/// Диапазон яркости (luma range).
/// </summary>
public enum ColorRange : byte
{
    /// <summary>Неизвестный диапазон.</summary>
    Unknown = 0,

    /// <summary>Limited/TV range: Y [16-235], UV [16-240] для 8-bit.</summary>
    Limited = 1,

    /// <summary>Full/PC range: [0-255] для 8-bit.</summary>
    Full = 2,
}

/// <summary>
/// Расположение семплов цветности (chroma location).
/// </summary>
public enum ChromaLocation : byte
{
    /// <summary>Неизвестное расположение.</summary>
    Unknown = 0,

    /// <summary>MPEG-2/4, H.264 (центр слева).</summary>
    Left = 1,

    /// <summary>MPEG-1, JPEG (центр).</summary>
    Center = 2,

    /// <summary>Верхний левый угол.</summary>
    TopLeft = 3,

    /// <summary>Верхний центр.</summary>
    Top = 4,

    /// <summary>Нижний левый угол.</summary>
    BottomLeft = 5,

    /// <summary>Нижний центр.</summary>
    Bottom = 6,
}
