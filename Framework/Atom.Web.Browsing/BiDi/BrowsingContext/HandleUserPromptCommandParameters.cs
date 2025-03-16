using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.handleUserPrompt.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="HandleUserPromptCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого обрабатывается пользовательский запрос.</param>
public class HandleUserPromptCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.handleUserPrompt";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого обрабатывается пользовательский запрос.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Указывает, следует ли принять пользовательский запрос (если true) или отменить его (если false).
    /// </summary>
    [JsonPropertyName("accept")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAccept { get; set; }

    /// <summary>
    /// Текст, отправленный в пользовательский запрос.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserText { get; set; }
}