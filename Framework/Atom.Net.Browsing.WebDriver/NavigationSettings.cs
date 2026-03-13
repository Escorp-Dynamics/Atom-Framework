namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Параметры навигации на страницу.
/// </summary>
public sealed class NavigationSettings
{
    /// <summary>
    /// HTML-контент, который будет подставлен вместо ответа сервера.
    /// Браузер перейдёт на указанный URL, но вместо реального ответа отрендерит этот HTML.
    /// </summary>
    public string? Body { get; init; }
}
