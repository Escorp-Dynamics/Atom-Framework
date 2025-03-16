namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read only proxy autoconfig used by the browser for this session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ManualProxyConfigurationResult"/> class.
/// </remarks>
/// <param name="proxy">The proxy autoconfig proxy configuration.</param>
public class ManualProxyConfigurationResult(ManualProxyConfiguration proxy) : ProxyConfigurationResult(proxy)
{
    /// <summary>
    /// Gets the address to be used to proxy HTTP commands.
    /// </summary>
    public string? HttpProxy => ProxyConfiguration.HttpProxy;

    /// <summary>
    /// Gets the address to be used to proxy HTTPS commands.
    /// </summary>
    public string? SslProxy => ProxyConfiguration.SslProxy;

    /// <summary>
    /// Gets the address to be used to proxy FTP commands.
    /// </summary>
    public string? FtpProxy => ProxyConfiguration.FtpProxy;

    /// <summary>
    /// Gets the address of a SOCKS proxy used to proxy commands.
    /// </summary>
    public string? SocksProxy => ProxyConfiguration.SocksProxy;

    /// <summary>
    /// Gets the version of the SOCKS proxy to be used.
    /// </summary>
    public int? SocksVersion => ProxyConfiguration.SocksVersion;

    /// <summary>
    /// Gets a list of addresses to be bypassed by the proxy.
    /// </summary>
    public IEnumerable<string>? NoProxyAddresses => ProxyConfiguration.NoProxyAddresses;

    private ManualProxyConfiguration ProxyConfiguration => ProxyConfigurationAs<ManualProxyConfiguration>();
}