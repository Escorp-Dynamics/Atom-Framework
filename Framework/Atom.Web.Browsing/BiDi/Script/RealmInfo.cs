using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing information about a script realm.
/// </summary>
[JsonConverter(typeof(RealmInfoJsonConverter))]
public class RealmInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RealmInfo"/> class.
    /// </summary>
    internal RealmInfo() { }

    /// <summary>
    /// Gets the ID of the realm.
    /// </summary>
    [JsonPropertyName("realm")]
    [JsonRequired]
    [JsonInclude]
    public string RealmId { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the origin of the realm.
    /// </summary>
    [JsonPropertyName("origin")]
    [JsonRequired]
    [JsonInclude]
    public string Origin { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the type of the realm.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonRequired]
    [JsonInclude]
    public RealmType Type { get; internal set; } = RealmType.Window;

    /// <summary>
    /// Gets this instance of a RealmInfo as a type-specific realm info.
    /// </summary>
    /// <typeparam name="T">The specific type of RealmInfo to return.</typeparam>
    /// <returns>This instance cast to the specified correct type.</returns>
    /// <exception cref="BiDiException">Thrown if this RealmInfo is not the specified type.</exception>
    public T As<T>() where T : RealmInfo => this is not T castValue ? throw new BiDiException($"This RealmInfo cannot be cast to {typeof(T)}") : castValue;
}