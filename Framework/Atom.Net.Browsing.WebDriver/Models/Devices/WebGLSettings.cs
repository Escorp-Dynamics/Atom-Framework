namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает vendor/renderer fingerprint WebGL.
/// </summary>
public sealed class WebGLSettings
{
    /// <summary>
    /// Получает или задаёт публичный вендор.
    /// </summary>
    public string? Vendor { get; set; }

    /// <summary>
    /// Получает или задаёт публичный renderer.
    /// </summary>
    public string? Renderer { get; set; }

    /// <summary>
    /// Получает или задаёт unmasked vendor.
    /// </summary>
    public string? UnmaskedVendor { get; set; }

    /// <summary>
    /// Получает или задаёт unmasked renderer.
    /// </summary>
    public string? UnmaskedRenderer { get; set; }

    /// <summary>
    /// Получает или задаёт версию WebGL.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Получает или задаёт версию shading language.
    /// </summary>
    public string? ShadingLanguageVersion { get; set; }
}