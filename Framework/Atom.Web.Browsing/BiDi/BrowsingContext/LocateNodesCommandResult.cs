using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат поиска узлов с использованием команды browserContext.locateNodes.
/// </summary>
public class LocateNodesCommandResult : CommandResult
{
    [JsonPropertyName("nodes")]
    [JsonRequired]
    [JsonInclude]
    internal List<RemoteValue> SerializableNodes { get; set; } = [];

    /// <summary>
    /// Коллекция найденных узлов.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<RemoteValue> Nodes => SerializableNodes.AsReadOnly();

    [JsonConstructor]
    internal LocateNodesCommandResult() { }
}