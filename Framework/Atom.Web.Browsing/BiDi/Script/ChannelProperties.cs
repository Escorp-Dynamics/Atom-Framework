using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Properties of a channel used to initiate passing information back from the browser from a preload script.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChannelProperties"/> class.
/// </remarks>
/// <param name="channelId">The ID of the channel.</param>
public class ChannelProperties(string channelId)
{
    /// <summary>
    /// Gets or sets the ID of the channel.
    /// </summary>
    [JsonPropertyName("channel")]
    public string ChannelId { get; set; } = channelId;

    /// <summary>
    /// Gets or sets the serialization options for the channel.
    /// </summary>
    [JsonPropertyName("serializationOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public SerializationOptions? SerializationOptions { get; set; }

    /// <summary>
    /// Gets or sets the result ownership for the channel.
    /// </summary>
    [JsonPropertyName("resultOwnership")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public ResultOwnership? ResultOwnership { get; set; }
}