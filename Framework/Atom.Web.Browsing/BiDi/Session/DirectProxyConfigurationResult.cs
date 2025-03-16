namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read only direct proxy configuration used by the browser for this session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DirectProxyConfigurationResult"/> class.
/// </remarks>
/// <param name="proxy">The direct proxy configuration.</param>
public class DirectProxyConfigurationResult(DirectProxyConfiguration proxy) : ProxyConfigurationResult(proxy) { }