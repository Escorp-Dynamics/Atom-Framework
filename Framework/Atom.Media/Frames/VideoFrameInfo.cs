using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Метаданные видеокадра.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct VideoFrameInfo
{
    /// <summary>Ширина кадра в пикселях.</summary>
    public required int Width { get; init; }

    /// <summary>Высота кадра в пикселях.</summary>
    public required int Height { get; init; }

    /// <summary>Формат пикселей.</summary>
    public required VideoPixelFormat PixelFormat { get; init; }

    /// <summary>Цветовое пространство.</summary>
    public ColorSpace ColorSpace { get; init; }

    /// <summary>Диапазон яркости.</summary>
    public ColorRange ColorRange { get; init; }

    /// <summary>Расположение семплов цветности.</summary>
    public ChromaLocation ChromaLocation { get; init; }

    /// <summary>Presentation timestamp в микросекундах.</summary>
    public long PtsUs { get; init; }

    /// <summary>Duration в микросекундах.</summary>
    public long DurationUs { get; init; }

    /// <summary>Флаг ключевого кадра (keyframe / I-frame).</summary>
    public bool IsKeyFrame { get; init; }

    /// <summary>
    /// Возвращает presentation timestamp как TimeSpan.
    /// </summary>
    public TimeSpan Pts
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(PtsUs);
    }

    /// <summary>
    /// Возвращает duration как TimeSpan.
    /// </summary>
    public TimeSpan Duration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(DurationUs);
    }

    /// <summary>
    /// Вычисляет размер буфера для данного формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CalculateBufferSize() => PixelFormat.CalculateFrameSize(Width, Height);
}
