namespace Atom.Web.Browsing;

/// <summary>
/// Представляет настройки контекста браузера.
/// </summary>
public class WebBrowserContextSettings : WebBrowserSettings, IWebBrowserContextSettings
{
    private static readonly Lazy<WebBrowserContextSettings> defaultSettings = new(() => new WebBrowserContextSettings(), true);

    /// <inheritdoc/>
    public static new IWebBrowserContextSettings Default => defaultSettings.Value;

    /// <inheritdoc/>
    public static TResult CreateFrom<TBase, TResult>(TBase baseSettings)
        where TBase : IWebBrowserSettings
        where TResult : IWebBrowserContextSettings, new() => new()
    {
        Logger = baseSettings.Logger,
        Cookies = baseSettings.Cookies,
        Handler = baseSettings.Handler,
        IsDOMEnabled = baseSettings.IsDOMEnabled,
        IsJavaScriptEnabled = baseSettings.IsJavaScriptEnabled,
        Proxy = baseSettings.Proxy,
    };
}