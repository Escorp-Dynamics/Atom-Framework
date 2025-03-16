using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет данные события, возникающего при закрытии пользовательского запроса.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="UserPromptClosedEventArgs"/>.
/// </remarks>
/// <param name="browsingContextId">Контекст просмотра, для которого был закрыт пользовательский запрос.</param>
/// <param name="isAccepted">Значение true, если пользовательский запрос был принят; false, если он был отменен.</param>
[method: JsonConstructor]
public class UserPromptClosedEventArgs(string browsingContextId, bool isAccepted) : BiDiEventArgs
{
    /// <summary>
    /// Идентификатор контекста просмотра, для которого был закрыт пользовательский запрос.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = browsingContextId;

    /// <summary>
    /// Указывает, был ли пользовательский запрос принят (true) или отменен (false).
    /// </summary>
    [JsonPropertyName("accepted")]
    [JsonRequired]
    [JsonInclude]
    public bool IsAccepted { get; internal set; } = isAccepted;

    /// <summary>
    /// Текст пользовательского запроса.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public string? UserText { get; internal set; }
}