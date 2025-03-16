using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a dedicated worker realm for executing script.
/// </summary>
public class DedicatedWorkerRealmInfo : RealmInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DedicatedWorkerRealmInfo"/> class.
    /// </summary>
    internal DedicatedWorkerRealmInfo() : base() { }

    /// <summary>
    /// Gets the read-only list of IDs of realms that are owners of this realm.
    /// </summary>
    public IList<string> Owners => SerializableOwners.AsReadOnly();

    /// <summary>
    /// Gets the list of IDs of realms that are owners of this realm for serialization purposes.
    /// </summary>
    [JsonPropertyName("owners")]
    internal List<string> SerializableOwners { get; } = [];
}