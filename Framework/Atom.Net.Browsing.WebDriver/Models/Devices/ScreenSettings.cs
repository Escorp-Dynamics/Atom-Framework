namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает модель screen.* и viewport-связанных значений.
/// </summary>
public sealed class ScreenSettings
{
    /// <summary>
    /// Получает или задаёт ширину экрана.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Получает или задаёт высоту экрана.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Получает или задаёт доступную ширину экрана.
    /// </summary>
    public int? AvailWidth { get; set; }

    /// <summary>
    /// Получает или задаёт доступную высоту экрана.
    /// </summary>
    public int? AvailHeight { get; set; }

    /// <summary>
    /// Получает или задаёт глубину цвета.
    /// </summary>
    public int? ColorDepth { get; set; }

    /// <summary>
    /// Получает или задаёт глубину пикселя.
    /// </summary>
    public int? PixelDepth { get; set; }
}