using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.reload.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ReloadCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра для перезагрузки.</param>
public class ReloadCommandParameters(string browsingContextId) : CommandParameters<NavigationResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.reload";

    /// <summary>
    /// Идентификатор контекста просмотра для перезагрузки.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Указывает, следует ли игнорировать кеш браузера при перезагрузке.
    /// </summary>
    [JsonPropertyName("ignoreCache")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsIgnoreCache { get; set; }

    /// <summary>
    /// Значение <see cref="ReadinessState"/>, для которого нужно ожидать во время перезагрузки.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadinessState? Wait { get; set; }
}