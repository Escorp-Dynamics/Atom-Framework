namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing an autodetect proxy to be used by the browser.
/// </summary>
public class AutoDetectProxyConfiguration : ProxyConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AutoDetectProxyConfiguration"/> class.
    /// </summary>
    public AutoDetectProxyConfiguration() : base(ProxyType.AutoDetect, JsonContext.Default.AutoDetectProxyConfiguration) { }
}