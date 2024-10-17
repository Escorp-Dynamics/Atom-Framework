using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки контекста браузера.
/// </summary>
public class WebBrowserContextSettings : WebBrowserSettings, IWebBrowserContextSettings
{
    /// <summary>
    /// Настройки контекста браузера по умолчанию.
    /// </summary>
    public static new WebBrowserContextSettings Default => new();

    /// <summary>
    /// Получает настройки контекста на базе настроек веб-браузера.
    /// </summary>
    /// <param name="browserSettings">Настройки веб-браузера.</param>
    /// <returns>Настройки контекста веб-браузера.</returns>
    public static WebBrowserContextSettings FromBrowserSettings([NotNull] IWebBrowserSettings browserSettings)
    {
        var settings = Default;

        settings.Handler = browserSettings.Handler;
        settings.Proxy = browserSettings.Proxy;
        settings.Cookies = browserSettings.Cookies;
        settings.IsDOMEnabled = browserSettings.IsDOMEnabled;
        settings.IsJavaScriptEnabled = browserSettings.IsJavaScriptEnabled;

        return settings;
    }
}