namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает low/high entropy client hints браузера.
/// </summary>
public sealed class ClientHintsSettings
{
    /// <summary>
    /// Получает или задаёт список брендов.
    /// </summary>
    public IEnumerable<ClientHintBrand>? Brands { get; set; }

    /// <summary>
    /// Получает или задаёт полный список брендов и версий.
    /// </summary>
    public IEnumerable<ClientHintBrand>? FullVersionList { get; set; }

    /// <summary>
    /// Получает или задаёт платформу.
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Получает или задаёт версию платформы.
    /// </summary>
    public string? PlatformVersion { get; set; }

    /// <summary>
    /// Получает или задаёт признак мобильного устройства.
    /// </summary>
    public bool? Mobile { get; set; }

    /// <summary>
    /// Получает или задаёт архитектуру CPU.
    /// </summary>
    public string? Architecture { get; set; }

    /// <summary>
    /// Получает или задаёт модель устройства.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Получает или задаёт разрядность платформы.
    /// </summary>
    public string? Bitness { get; set; }
}