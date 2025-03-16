using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Result for creating a new session using the session.new command.
/// </summary>
public class NewCommandResult : CommandResult
{
    [JsonConstructor]
    internal NewCommandResult() { }

    /// <summary>
    /// Gets the ID of the session.
    /// </summary>
    [JsonPropertyName("sessionId")]
    [JsonRequired]
    [JsonInclude]
    public string SessionId { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the actual capabilities used in this session.
    /// </summary>
    [JsonPropertyName("capabilities")]
    [JsonRequired]
    [JsonInclude]
    public CapabilitiesResult Capabilities { get; internal set; } = new();
}