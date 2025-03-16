using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// A script target for a realm.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RealmTarget"/> class.
/// </remarks>
/// <param name="realmId">The ID of the realm.</param>
[method: JsonConstructor]
public class RealmTarget(string realmId) : Target
{
    /// <summary>
    /// Gets the ID of the realm.
    /// </summary>
    [JsonPropertyName("realm")]
    [JsonRequired]
    [JsonInclude]
    public string RealmId { get; internal set; } = realmId;
}