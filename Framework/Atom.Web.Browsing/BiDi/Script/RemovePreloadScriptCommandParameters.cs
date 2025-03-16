using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides parameters for the script.removePreloadScript command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RemovePreloadScriptCommandParameters"/> class.
/// </remarks>
/// <param name="preloadScriptId">The ID of the preload script to remove.</param>
public class RemovePreloadScriptCommandParameters(string preloadScriptId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "script.removePreloadScript";

    /// <summary>
    /// Gets or sets the ID of the preload script to remove.
    /// </summary>
    [JsonPropertyName("script")]
    public string PreloadScriptId { get; set; } = preloadScriptId;
}