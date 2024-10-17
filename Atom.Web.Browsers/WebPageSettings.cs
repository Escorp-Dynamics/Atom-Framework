using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки веб-страницы.
/// </summary>
public class WebPageSettings : WebBrowserContextSettings, IWebPageSettings
{
    /// <summary>
    /// Настройки веб-страницы по умолчанию.
    /// </summary>
    public static new WebPageSettings Default => new();

    /// <summary>
    /// Получает настройки веб-страницы на базе настроек контекста веб-браузера.
    /// </summary>
    /// <param name="contextSettings">Настройки контекста веб-браузера.</param>
    /// <returns>Настройки веб-страницы.</returns>
    public static WebPageSettings FromContextSettings([NotNull] IWebBrowserContextSettings contextSettings)
    {
        var settings = Default;

        settings.Handler = contextSettings.Handler;
        settings.Proxy = contextSettings.Proxy;
        settings.Cookies = contextSettings.Cookies;
        settings.IsDOMEnabled = contextSettings.IsDOMEnabled;
        settings.IsJavaScriptEnabled = contextSettings.IsJavaScriptEnabled;

        return settings;
    }
}