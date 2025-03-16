namespace Atom.Web.Browsing;

/// <summary>
/// Представляет настройки окна браузера.
/// </summary>
public class WebWindowSettings : WebBrowserContextSettings, IWebWindowSettings
{
    private static readonly Lazy<WebWindowSettings> defaultSettings = new(() => new WebWindowSettings(), true);

    /// <inheritdoc/>
    public static new IWebWindowSettings Default => defaultSettings.Value;

    /// <inheritdoc/>
    public static new TResult CreateFrom<TBase, TResult>(TBase baseSettings)
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