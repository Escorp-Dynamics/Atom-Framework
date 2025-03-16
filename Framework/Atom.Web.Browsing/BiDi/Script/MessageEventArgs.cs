using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing event data for the event raised when a preload script sends a message to the client.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageEventArgs"/> class.
/// </remarks>
/// <param name="channelId">The ID of the channel used for this message.</param>
/// <param name="data">The data for this message.</param>
/// <param name="source">The source for this message.</param>
[method: JsonConstructor]
public class MessageEventArgs(string channelId, RemoteValue data, Source source) : BiDiEventArgs
{
    /// <summary>
    /// Gets the ID of the channel used for this message.
    /// </summary>
    [JsonPropertyName("channel")]
    public string ChannelId { get; } = channelId;

    /// <summary>
    /// Gets the data for this message.
    /// </summary>
    [JsonPropertyName("data")]
    public RemoteValue Data { get; } = data;

    /// <summary>
    /// Gets the source for this message.
    /// </summary>
    [JsonPropertyName("source")]
    public Source Source { get; } = source;
}