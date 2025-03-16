using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Result for getting realms using the script.getRealms command.
/// </summary>
public class GetRealmsCommandResult : CommandResult
{
    [JsonConstructor]
    internal GetRealmsCommandResult() { }

    /// <summary>
    /// Gets a read-only list of information about the realms.
    /// </summary>
    [JsonIgnore]
    public IList<RealmInfo> Realms => SerializableRealms.AsReadOnly();

    /// <summary>
    /// Gets or sets the list of information about the realms for serialization purposes.
    /// </summary>
    [JsonPropertyName("realms")]
    [JsonInclude]
    internal List<RealmInfo> SerializableRealms { get; set; } = [];
}