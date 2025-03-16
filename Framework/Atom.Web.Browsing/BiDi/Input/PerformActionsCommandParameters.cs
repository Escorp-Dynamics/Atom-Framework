using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет параметры для команды input.performActions.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="PerformActionsCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, в котором будут выполняться действия.</param>
public class PerformActionsCommandParameters(string browsingContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Название метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "input.performActions";

    /// <summary>
    /// Идентификатор контекста просмотра, в котором будут выполняться действия.
    /// </summary>
    [JsonPropertyName("context")]
    public string ContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Коллекция действий для выполнения.
    /// </summary>
    public IEnumerable<SourceActions> Actions { get; } = [];
}