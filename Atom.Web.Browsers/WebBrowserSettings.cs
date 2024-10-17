using System.Net;
using Atom.Web.Proxies;

namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки браузера.
/// </summary>
public class WebBrowserSettings : IWebBrowserSettings
{
    /// <inheritdoc/>
    public HttpClientHandler Handler { get; set; } = new HttpClientHandler();

    /// <inheritdoc/>
    public Proxy? Proxy
    {
        get => Handler.Proxy as Proxy;
        set => Handler.Proxy = value;
    }

    /// <inheritdoc/>
    public IEnumerable<Cookie> Cookies { get; set; } = [];

    /// <inheritdoc/>
    public bool IsDOMEnabled { get; set; } = true;

    /// <inheritdoc/>
    public bool IsJavaScriptEnabled { get; set; } = true;

    /// <summary>
    /// Настройки браузера по умолчанию.
    /// </summary>
    public static WebBrowserSettings Default => new();
}