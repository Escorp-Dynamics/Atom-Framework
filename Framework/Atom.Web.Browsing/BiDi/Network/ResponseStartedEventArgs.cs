using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Object containing event data for events raised by before a network request is sent.
/// </summary>
public class ResponseStartedEventArgs : BaseNetworkEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseStartedEventArgs"/> class.
    /// </summary>
    public ResponseStartedEventArgs() : base() { }

    /// <summary>
    /// Gets the initiator of the request.
    /// </summary>
    [JsonPropertyName("response")]
    [JsonRequired]
    [JsonInclude]
    public ResponseData Response { get; internal set; } = new();
}