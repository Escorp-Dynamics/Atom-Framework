using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Result for adding a preload script using the script.addPreloadScript command.
/// </summary>
public class AddPreloadScriptCommandResult : CommandResult
{
    [JsonConstructor]
    internal AddPreloadScriptCommandResult() { }

    /// <summary>
    /// Gets the ID of the preload script.
    /// </summary>
    [JsonPropertyName("script")]
    [JsonInclude]
    public string PreloadScriptId { get; internal set; } = string.Empty;
}