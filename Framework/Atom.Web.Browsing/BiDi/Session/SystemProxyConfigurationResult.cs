namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read only representation of the system proxy configuration used by the browser for this session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SystemProxyConfigurationResult"/> class.
/// </remarks>
/// <param name="proxy">The system proxy configuration.</param>
public class SystemProxyConfigurationResult(SystemProxyConfiguration proxy) : ProxyConfigurationResult(proxy);