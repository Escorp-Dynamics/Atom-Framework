using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Options for serialization of script objects.
/// </summary>
public class SerializationOptions
{
    /// <summary>
    /// Gets or sets the maximum depth when serializing DOM nodes from script execution.
    /// </summary>
    [JsonPropertyName("maxDomDepth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxDomDepth { get; set; }

    /// <summary>
    /// Gets or sets the maximum depth when serializing script objects from script execution.
    /// </summary>
    [JsonPropertyName("maxObjectDepth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxObjectDepth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating which shadow trees to serializes when serializing nodes from script execution.
    /// </summary>
    [JsonPropertyName("includeShadowTree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IncludeShadowTreeSerializationOption? IncludeShadowTree { get; set; }
}