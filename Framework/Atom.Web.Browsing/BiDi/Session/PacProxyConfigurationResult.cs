namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read only proxy autoconfig used by the browser for this session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PacProxyConfigurationResult"/> class.
/// </remarks>
/// <param name="proxy">The proxy autoconfig proxy configuration.</param>
public class PacProxyConfigurationResult(PacProxyConfiguration proxy) : ProxyConfigurationResult(proxy)
{
    /// <summary>
    /// Gets the URL to the proxy autoconfig (PAC) settings.
    /// </summary>
    public Uri ProxyAutoConfigUrl => ProxyConfigurationAs<PacProxyConfiguration>().ProxyAutoConfigUrl;
}