using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The abstract base class for a value that can contain either a string or a byte array.
/// </summary>
[JsonDerivedType(typeof(UrlPatternPattern))]
[JsonDerivedType(typeof(UrlPatternString))]
public class UrlPattern
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UrlPattern"/> class.
    /// </summary>
    /// <param name="patternType">The type of pattern to create.</param>
    protected UrlPattern(UrlPatternType patternType) => Type = patternType;

    /// <summary>
    /// Gets the type of this UrlPattern.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public UrlPatternType Type { get; }
}