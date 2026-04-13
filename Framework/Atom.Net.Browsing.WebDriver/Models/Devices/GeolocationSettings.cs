namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает координаты и точность navigator.geolocation.
/// </summary>
public sealed class GeolocationSettings
{
    /// <summary>
    /// Получает или задаёт широту.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Получает или задаёт долготу.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Получает или задаёт точность в метрах.
    /// </summary>
    public double? Accuracy { get; set; }
}