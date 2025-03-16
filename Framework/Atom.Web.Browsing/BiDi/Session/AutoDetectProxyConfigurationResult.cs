namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a read only autodetect proxy configuration used by the browser for this session.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AutoDetectProxyConfigurationResult"/> class.
/// </remarks>
/// <param name="proxy">The autodetect proxy configuration.</param>
public class AutoDetectProxyConfigurationResult(AutoDetectProxyConfiguration proxy) : ProxyConfigurationResult(proxy) { }