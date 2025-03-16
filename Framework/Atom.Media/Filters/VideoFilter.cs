using System.Drawing;

namespace Atom.Media.Filters;

/// <summary>
/// Представляет базовый класс для реализации видеофильтров.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="VideoFilter"/>.
/// </remarks>
/// <param name="duration">Длительность эффекта.</param>
public abstract class VideoFilter(TimeSpan duration) : Filter(duration), IVideoFilter
{
    internal int FrameRate { get; set; }
    internal Size Resolution { get; set; }
}