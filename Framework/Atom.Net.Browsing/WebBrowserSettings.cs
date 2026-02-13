using System.Net;

namespace Atom.Net.Browsing;

/// <summary>
/// Представляет настройки для <see cref="IWebBrowser"/>.
/// </summary>
public class WebBrowserSettings
{
    /// <summary>
    /// Прокси для запросов.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Настройки окна браузера.
    /// </summary>
    public WebWindowSettings? WindowSettings { get; set; }
}