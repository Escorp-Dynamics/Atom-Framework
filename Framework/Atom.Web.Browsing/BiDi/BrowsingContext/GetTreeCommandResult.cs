using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат получения дерева контекстов просмотра с использованием команды browserContext.getTree.
/// </summary>
public class GetTreeCommandResult : CommandResult
{
    [JsonPropertyName("contexts")]
    [JsonRequired]
    [JsonInclude]
    internal IList<BrowsingContextInfo> SerializableContextTree { get; set; } = [];

    /// <summary>
    /// Дерево контекстов просмотра.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<BrowsingContextInfo> ContextTree => SerializableContextTree.AsReadOnly();

    [JsonConstructor]
    internal GetTreeCommandResult() { }
}