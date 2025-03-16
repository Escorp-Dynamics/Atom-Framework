using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.close.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CloseCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра для закрытия.</param>
public class CloseCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.close";

    /// <summary>
    /// Идентификатор контекста просмотра для закрытия.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Указывает, нужно ли запрашивать подтверждение выгрузки страницы при закрытии контекста просмотра.
    /// </summary>
    [JsonPropertyName("promptUnload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsPromptUnload { get; set; }
}