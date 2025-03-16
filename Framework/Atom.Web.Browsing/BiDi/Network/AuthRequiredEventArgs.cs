using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Object containing event data for events raised by before a network request is sent.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AuthRequiredEventArgs"/> class.
/// </remarks>
public class AuthRequiredEventArgs() : BaseNetworkEventArgs()
{
    /// <summary>
    /// Gets the initiator of the request.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public ResponseData Response { get; internal set; } = new();
}