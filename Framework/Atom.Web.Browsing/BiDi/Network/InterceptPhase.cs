using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Values used for setting up intercepts for network traffic.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<InterceptPhase>))]
public enum InterceptPhase
{
    /// <summary>
    /// Network traffic will be intercepted before a request is sent.
    /// </summary>
    [JsonEnumValue("beforeRequestSent")]
    BeforeRequestSent,

    /// <summary>
    /// Network traffic will be intercepted when a response is received, but before sent to the browser.
    /// </summary>
    [JsonEnumValue("responseStarted")]
    ResponseStarted,

    /// <summary>
    /// Network traffic will be intercepted when a response would require authorization.
    /// </summary>
    [JsonEnumValue("authRequired")]
    AuthRequired,
}