using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a script target that is a browsing context.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContextTarget"/> class.
/// </remarks>
/// <param name="browsingContextId">The ID of the browsing context of the script target.</param>
[method: JsonConstructor]
public class ContextTarget(string browsingContextId) : Target
{
    /// <summary>
    /// Gets the ID of the browsing context used as a script target.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = browsingContextId;

    /// <summary>
    /// Gets or sets the name of the sandbox.
    /// </summary>
    [JsonPropertyName("sandbox")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? Sandbox { get; set; }
}