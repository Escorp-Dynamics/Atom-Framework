using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The abstract base class for a value that can contain either a string or a byte array.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UrlPatternString"/> class.
/// </remarks>
/// <param name="pattern">The pattern to match.</param>
public class UrlPatternString(string pattern) : UrlPattern(UrlPatternType.String)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UrlPatternString"/> class.
    /// </summary>
    public UrlPatternString() : this(string.Empty) { }

    /// <summary>
    /// Gets or sets the pattern to match.
    /// </summary>
    [JsonPropertyName("pattern")]
    [JsonInclude]
    public string Pattern { get; set; } = pattern;
}