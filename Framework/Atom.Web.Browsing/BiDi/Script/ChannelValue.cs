using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Value to be used as argument to a preload script.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChannelValue"/> class.
/// </remarks>
/// <param name="value">The properties for this ChannelValue.</param>
public class ChannelValue(ChannelProperties value) : ArgumentValue
{
    /// <summary>
    /// Gets the type of this ChannelValue.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; } = "channel";

    /// <summary>
    /// Gets the value of this ChannelValue.
    /// </summary>
    [JsonPropertyName("value")]
    public ChannelProperties Value { get; } = value;
}