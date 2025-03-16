using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Result for getting the status of a remote end using the session.status command.
/// </summary>
public class StatusCommandResult : CommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommandResult"/> class.
    /// </summary>
    [JsonConstructor]
    internal StatusCommandResult() { }

    /// <summary>
    /// Gets a value indicating whether the remote end is able to create new sessions.
    /// </summary>
    [JsonPropertyName("ready")]
    [JsonRequired]
    [JsonInclude]
    public bool IsReady { get; internal set; }

    /// <summary>
    /// Gets a message about the status from the remote end.
    /// </summary>
    [JsonPropertyName("message")]
    [JsonRequired]
    [JsonInclude]
    public string Message { get; internal set; } = string.Empty;
}