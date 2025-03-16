using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Result for installing a web extension using the webExtension.install command.
/// </summary>
public class InstallCommandResult : CommandResult
{
    [JsonConstructor]
    internal InstallCommandResult() { }

    /// <summary>
    /// Gets the ID of the installed extension as specified in the extension manifest.
    /// </summary>
    [JsonPropertyName("extension")]
    [JsonRequired]
    [JsonInclude]
    public string ExtensionId { get; internal set; } = string.Empty;
}