using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// A class providing credentials for authorization.
/// </summary>
public class AuthChallenge
{

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthChallenge"/> class.
    /// </summary>
    [JsonConstructor]
    internal AuthChallenge() { }

    /// <summary>
    /// Gets the scheme of the authentication challenge.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Scheme { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the realm of the authentication challenge.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Realm { get; internal set; } = string.Empty;
}