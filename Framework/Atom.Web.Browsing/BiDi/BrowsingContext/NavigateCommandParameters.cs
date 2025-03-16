using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.navigate.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="NavigateCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра для навигации.</param>
/// <param name="url">URL, по которому нужно перейти.</param>
public class NavigateCommandParameters(string browsingContextId, Uri url) : CommandParameters<NavigationResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.navigate";

    /// <summary>
    /// Идентификатор контекста просмотра для навигации.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// URL, по которому нужно перейти.
    /// </summary>
    [JsonRequired]
    public Uri Url { get; set; } = url;

    /// <summary>
    /// Значение <see cref="ReadinessState"/>, для которого нужно ожидать во время навигации.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadinessState? Wait { get; set; }
}