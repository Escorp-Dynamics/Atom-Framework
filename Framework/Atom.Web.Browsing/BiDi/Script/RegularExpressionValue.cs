using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a regular expression.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RegularExpressionValue"/> class with a given pattern and flags.
/// </remarks>
/// <param name="pattern">The pattern for the regular expression.</param>
/// <param name="flags">The flags used in the regular expression.</param>
public class RegularExpressionValue(string pattern, string? flags)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegularExpressionValue"/> class with a given pattern.
    /// </summary>
    /// <param name="pattern">The pattern for the regular expression.</param>
    [JsonConstructor]
    public RegularExpressionValue(string pattern) : this(pattern, null) { }

    /// <summary>
    /// Gets the pattern used in the regular expression.
    /// </summary>
    [JsonPropertyName("pattern")]
    [JsonRequired]
    [JsonInclude]
    public string Pattern { get; internal set; } = pattern;

    /// <summary>
    /// Gets the flags used in the regular expression.
    /// </summary>
    [JsonPropertyName("flags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? Flags { get; internal set; } = flags;

    /// <summary>
    /// Computes a hash code for this RegularExpressionValue.
    /// </summary>
    /// <returns>A hash code for the this RegularExpressionValue.</returns>
    public override int GetHashCode() => HashCode.Combine(Pattern, Flags);

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><see langword="true"/> if the objects are equal; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not RegularExpressionValue other) return false;

        var areEqual = Pattern == other.Pattern;
        return Flags is null && other.Flags is null ? areEqual : areEqual && Flags == other.Flags;
    }
}
