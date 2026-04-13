namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает fingerprint для navigator.connection.
/// </summary>
public sealed class NetworkInfoSettings
{
    /// <summary>
    /// Получает или задаёт effective connection type.
    /// </summary>
    public string EffectiveType { get; set; } = "4g";

    /// <summary>
    /// Получает или задаёт тип транспорта соединения.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Получает или задаёт RTT в миллисекундах.
    /// </summary>
    public double Rtt { get; set; } = 50;

    /// <summary>
    /// Получает или задаёт пропускную способность в Мбит/с.
    /// </summary>
    public double Downlink { get; set; } = 10;

    /// <summary>
    /// Получает или задаёт признак режима экономии трафика.
    /// </summary>
    public bool EnableDataSaving { get; set; }
}