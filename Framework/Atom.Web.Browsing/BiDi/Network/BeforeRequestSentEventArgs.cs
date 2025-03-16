using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Object containing event data for events raised by before a network request is sent.
/// </summary>
public class BeforeRequestSentEventArgs : BaseNetworkEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BeforeRequestSentEventArgs"/> class.
    /// </summary>
    public BeforeRequestSentEventArgs() : base() { }

    /// <summary>
    /// Gets the initiator of the request.
    /// </summary>
    [JsonPropertyName("initiator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public Initiator? Initiator { get; internal set; }
}