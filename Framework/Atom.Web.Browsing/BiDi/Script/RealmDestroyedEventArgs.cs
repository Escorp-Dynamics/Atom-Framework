using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing event data for the event raised when a script realm is destroyed.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RealmDestroyedEventArgs"/> class.
/// </remarks>
/// <param name="realmId">The ID of the realm being destroyed.</param>
[method: JsonConstructor]
public class RealmDestroyedEventArgs(string realmId) : BiDiEventArgs
{
    /// <summary>
    /// Gets the ID of the realm being destroyed.
    /// </summary>
    [JsonPropertyName("realm")]
    [JsonRequired]
    [JsonInclude]
    public string RealmId { get; internal set; } = realmId;
}