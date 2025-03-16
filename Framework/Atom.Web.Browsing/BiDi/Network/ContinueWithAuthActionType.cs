using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The enumerated value of actions allowed when using the network.continueWithAuth command.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ContinueWithAuthActionType>))]
public enum ContinueWithAuthActionType
{
    /// <summary>
    /// The command will perform the default action.
    /// </summary>
    Default,

    /// <summary>
    /// The command will cancel the auth request.
    /// </summary>
    Cancel,

    /// <summary>
    /// The command will use the provided credentials.
    /// </summary>
    [JsonEnumValue("provideCredentials")]
    ProvideCredentials,
}