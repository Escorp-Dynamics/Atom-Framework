using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Object representing a manual proxy to be used by the browser.
/// </summary>
public class ManualProxyConfiguration : ProxyConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManualProxyConfiguration"/> class.
    /// </summary>
    public ManualProxyConfiguration() : base(ProxyType.Manual, JsonContext.Default.ManualProxyConfiguration) { }

    /// <summary>
    /// Gets or sets the address to be used to proxy HTTP commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HttpProxy { get; set; }

    /// <summary>
    /// Gets or sets the address to be used to proxy HTTPS commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SslProxy { get; set; }

    /// <summary>
    /// Gets or sets the address to be used to proxy FTP commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FtpProxy { get; set; }

    /// <summary>
    /// Gets or sets the address of a SOCKS proxy used to proxy commands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SocksProxy { get; set; }

    /// <summary>
    /// Gets or sets the version of the SOCKS proxy to be used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SocksVersion { get; set; }

    /// <summary>
    /// Gets or sets a list of addresses to be bypassed by the proxy.
    /// </summary>
    [JsonPropertyName("noProxy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? NoProxyAddresses { get; set; }
}