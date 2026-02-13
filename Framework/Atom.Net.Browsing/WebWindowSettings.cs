using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net;

namespace Atom.Net.Browsing;

/// <summary>
/// Представляет настройки для <see cref="IWebWindow"/>.
/// </summary>
public sealed class WebWindowSettings
{
    /// <summary>
    /// Прокси для запросов.
    /// </summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Размер окна.
    /// </summary>
    public Size Size { get; set; }

    /// <summary>
    /// Позиция окна на экране.
    /// </summary>
    public Point Position { get; set; }

    /// <summary>
    /// Определяет или задаёт, будет ли окно открыто в режиме инкогнито.
    /// </summary>
    public bool AsIncognito { get; init; }

    private WebWindowSettings(WebBrowserSettings settings) => Proxy = settings.Proxy;

    /// <summary>
    /// Преобразует <see cref="WebBrowserSettings"/> в <see cref="WebWindowSettings"/>.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    public static WebWindowSettings FromWebBrowserSettings(WebBrowserSettings settings) => (WebWindowSettings)settings;

    /// <summary>
    /// Преобразует <see cref="WebBrowserSettings"/> в <see cref="WebWindowSettings"/>.
    /// </summary>
    /// <param name="settings">Настройки браузера.</param>
    public static explicit operator WebWindowSettings([NotNull] WebBrowserSettings settings) => new(settings);
}