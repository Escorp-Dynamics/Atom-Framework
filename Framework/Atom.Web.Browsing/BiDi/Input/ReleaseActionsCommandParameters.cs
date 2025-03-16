using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет параметры для команды input.releaseActions.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ReleaseActionsCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого необходимо освободить ожидающие действия.</param>
public class ReleaseActionsCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Название метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "input.releaseActions";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого необходимо освободить ожидающие действия.
    /// </summary>
    [JsonPropertyName("context")]
    public string ContextId { get; set; } = browsingContextId;
}