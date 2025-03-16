namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a direct connection proxy to be used by the browser.
/// </summary>
public class DirectProxyConfiguration : ProxyConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectProxyConfiguration"/> class.
    /// </summary>
    public DirectProxyConfiguration() : base(ProxyType.Direct, JsonContext.Default.DirectProxyConfiguration) { }
}