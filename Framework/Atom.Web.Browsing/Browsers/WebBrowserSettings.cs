using System.Net;
using Atom.Architect.Reactive;
using Atom.Web.Browsing.Fingerprints;
using Atom.Web.Proxies;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет настройки браузера.
/// </summary>
public partial class WebBrowserSettings : IWebBrowserSettings
{
    private static readonly Lazy<WebBrowserSettings> defaultSettings = new(() => new WebBrowserSettings
    {
        IsDOMEnabled = true,
        IsJavaScriptEnabled = true,
    }, true);

    /// <inheritdoc/>
    [Reactively]
    private ILogger? logger;

    /// <inheritdoc/>
    [Reactively]
    private HttpClientHandler? handler;

    /// <inheritdoc/>
    [Reactively]
    private Proxy? proxy;

    /// <inheritdoc/>
    [Reactively]
    private IEnumerable<Cookie> cookies = [];

    /// <inheritdoc/>
    [Reactively]
    private IWebFingerprint? fingerprint;

    /// <inheritdoc/>
    [Reactively]
    private bool isDOMEnabled;

    /// <inheritdoc/>
    [Reactively]
    private bool isJavaScriptEnabled;

    /// <summary>
    /// Настройки браузера по умолчанию.
    /// </summary>
    public static IWebBrowserSettings Default => defaultSettings.Value;
}