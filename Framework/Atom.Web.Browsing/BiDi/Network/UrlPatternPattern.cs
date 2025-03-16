using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The abstract base class for a value that can contain either a string or a byte array.
/// </summary>
public class UrlPatternPattern : UrlPattern
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UrlPatternPattern"/> class.
    /// </summary>
    public UrlPatternPattern() : base(UrlPatternType.Pattern) { }

    /// <summary>
    /// Gets or sets the protocol to match.
    /// </summary>
    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    /// <summary>
    /// Gets or sets the host name to match.
    /// </summary>
    [JsonPropertyName("hostname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostName { get; set; }

    /// <summary>
    /// Gets or sets the port to match.
    /// </summary>
    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Port { get; set; }

    /// <summary>
    /// Gets or sets the path name to match.
    /// </summary>
    [JsonPropertyName("pathname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PathName { get; set; }

    /// <summary>
    /// Gets or sets the search to match.
    /// </summary>
    [JsonPropertyName("search")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Search { get; set; }
}