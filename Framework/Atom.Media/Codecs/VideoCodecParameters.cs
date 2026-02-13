using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Параметры видеокодека.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct VideoCodecParameters
{
    /// <summary>Ширина кадра.</summary>
    public required int Width { get; init; }

    /// <summary>Высота кадра.</summary>
    public required int Height { get; init; }

    /// <summary>Формат пикселей.</summary>
    public required VideoPixelFormat PixelFormat { get; init; }

    /// <summary>Частота кадров (fps).</summary>
    public double FrameRate { get; init; }

    /// <summary>Битрейт в битах в секунду (для encoder).</summary>
    public long BitRate { get; init; }

    /// <summary>GOP size (расстояние между ключевыми кадрами).</summary>
    public int GopSize { get; init; }

    /// <summary>Количество B-frames.</summary>
    public int BFrames { get; init; }

    /// <summary>Качество (0-100, для CRF режима).</summary>
    public int Quality { get; init; }

    /// <summary>Цветовое пространство.</summary>
    public ColorSpace ColorSpace { get; init; }

    /// <summary>Диапазон яркости.</summary>
    public ColorRange ColorRange { get; init; }

    /// <summary>Профиль кодека (зависит от кодека).</summary>
    public int Profile { get; init; }

    /// <summary>Уровень кодека (зависит от кодека).</summary>
    public int Level { get; init; }

    /// <summary>Количество потоков для многопоточного кодирования.</summary>
    public int ThreadCount { get; init; }

    /// <summary>Extra data (SPS/PPS для H.264, и т.д.).</summary>
    public ReadOnlyMemory<byte> ExtraData { get; init; }
}
