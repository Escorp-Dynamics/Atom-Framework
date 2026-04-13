namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет канал распространения браузера.
/// </summary>
public enum WebBrowserChannel
{
    /// <summary>
    /// Стабильный канал.
    /// </summary>
    Stable,

    /// <summary>
    /// Бета-канал.
    /// </summary>
    Beta,

    /// <summary>
    /// Канал для ранних dev-сборок.
    /// </summary>
    Dev,
}