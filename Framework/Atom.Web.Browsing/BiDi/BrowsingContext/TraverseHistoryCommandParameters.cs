using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.traverseHistory.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="TraverseHistoryCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого нужно перемещаться по истории.</param>
/// <param name="delta">Количество позиций, вперёд или назад, для перемещения по истории браузера.</param>
public class TraverseHistoryCommandParameters(string browsingContextId, long delta) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.traverseHistory";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого нужно перемещаться по истории.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonInclude]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Количество записей в истории, по которым нужно перемещаться. Положительные значения перемещают вперёд по истории; отрицательные значения перемещают назад по истории.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long Delta { get; set; } = delta;
}