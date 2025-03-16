using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Session;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет данные события, возникающего при открытии пользовательского запроса.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="UserPromptOpenedEventArgs"/>.
/// </remarks>
/// <param name="browsingContextId">Контекст просмотра, для которого был открыт пользовательский запрос.</param>
/// <param name="promptType">Тип пользовательского запроса.</param>
/// <param name="message">Сообщение, отображаемое в пользовательском запросе.</param>
[method: JsonConstructor]
public class UserPromptOpenedEventArgs(string browsingContextId, UserPromptType promptType, string message) : BiDiEventArgs
{
    /// <summary>
    /// Идентификатор контекста просмотра, для которого был открыт пользовательский запрос.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = browsingContextId;

    /// <summary>
    /// Тип обработчика запроса для этого события.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public UserPromptHandlerType Handler { get; internal set; }

    /// <summary>
    /// Тип открытого пользовательского запроса.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonRequired]
    [JsonInclude]
    public UserPromptType PromptType { get; internal set; } = promptType;

    /// <summary>
    /// Сообщение, отображаемое в пользовательском запросе.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Message { get; internal set; } = message;

    /// <summary>
    /// Значение по умолчанию для пользовательского запроса, если оно есть.
    /// </summary>
    [JsonInclude]
    public string? DefaultValue { get; internal set; }
}