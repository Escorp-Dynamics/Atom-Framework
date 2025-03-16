using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.create.
/// </summary>
public class GetTreeCommandParameters : CommandParameters<GetTreeCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.getTree";

    /// <summary>
    /// Максимальная глубину обхода дерева.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxDepth { get; set; }

    /// <summary>
    /// Идентификатор контекста просмотра, используемого в качестве корня дерева.
    /// </summary>
    [JsonPropertyName("root")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootBrowsingContextId { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GetTreeCommandParameters"/>.
    /// </summary>
    public GetTreeCommandParameters() { }
}