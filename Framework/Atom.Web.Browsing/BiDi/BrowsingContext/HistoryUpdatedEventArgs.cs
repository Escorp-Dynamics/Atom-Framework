using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет объект, содержащий данные события browsingContext.historyUpdated.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HistoryUpdatedEventArgs"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, соответствующего элементу истории.</param>
/// <param name="url">URL элемента истории.</param>
[method: JsonConstructor]
public class HistoryUpdatedEventArgs(string browsingContextId, Uri url) : BiDiEventArgs
{
    /// <summary>
    /// Идентификатор контекста просмотра в записи истории.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = browsingContextId;

    /// <summary>
    /// URL записи истории.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public Uri Url { get; internal set; } = url;
}