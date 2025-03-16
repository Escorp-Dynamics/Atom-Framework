using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет параметры для команды browsingContext.activate.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ActivateCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра для активации.</param>
public class ActivateCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browsingContext.activate";

    /// <summary>
    /// Идентификатор контекста просмотра для активации.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;
}