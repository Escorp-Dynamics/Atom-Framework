using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Result for adding an intercept for network traffic using the network.addIntercept command.
/// </summary>
public class AddInterceptCommandResult : CommandResult
{
    [JsonConstructor]
    internal AddInterceptCommandResult() { }

    /// <summary>
    /// Gets the screenshot image data as a base64-encoded string.
    /// </summary>
    [JsonPropertyName("intercept")]
    [JsonRequired]
    [JsonInclude]
    public string InterceptId { get; internal set; } = string.Empty;
}