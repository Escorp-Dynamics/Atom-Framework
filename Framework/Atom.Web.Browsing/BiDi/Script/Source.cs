using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing the source for a script.
/// </summary>
public class Source
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Source"/> class.
    /// </summary>
    [JsonConstructor]
    internal Source() { }

    /// <summary>
    /// Gets the ID of the realm for a script.
    /// </summary>
    [JsonPropertyName("realm")]
    [JsonRequired]
    [JsonInclude]
    public string RealmId { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the browsing context ID for a script.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? Context { get; internal set; }
}