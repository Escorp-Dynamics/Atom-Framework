using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a proxy autoconfig proxy to be used by the browser.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PacProxyConfiguration"/> class.
/// </remarks>
/// <param name="proxyAutoConfigUrl">The URL to the proxy autoconfig file.</param>
public class PacProxyConfiguration(Uri proxyAutoConfigUrl) : ProxyConfiguration(ProxyType.ProxyAutoConfig, JsonContext.Default.PacProxyConfiguration)
{
    /// <summary>
    /// Gets or sets the URL to the proxy autoconfig (PAC) settings.
    /// </summary>
    [JsonPropertyName("proxyAutoconfigUrl")]
    [JsonRequired]
    public Uri ProxyAutoConfigUrl { get; set; } = proxyAutoConfigUrl;
}