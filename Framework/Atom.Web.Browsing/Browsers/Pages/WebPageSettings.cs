namespace Atom.Web.Browsing;

/// <summary>
/// Представляет настройки веб-страницы.
/// </summary>
public class WebPageSettings : WebWindowSettings, IWebPageSettings
{
    private static readonly Lazy<WebPageSettings> defaultSettings = new(() => new WebPageSettings(), true);

    /// <inheritdoc/>
    public static new IWebPageSettings Default => defaultSettings.Value;

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