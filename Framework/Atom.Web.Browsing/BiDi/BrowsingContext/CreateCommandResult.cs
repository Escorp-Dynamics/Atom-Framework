using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат создания нового контекста просмотра с использованием команды browserContext.create.
/// </summary>
public class CreateCommandResult : CommandResult
{
    /// <summary>
    /// Идентификатор созданного контекста просмотра.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string BrowsingContextId { get; internal set; } = string.Empty;

    [JsonConstructor]
    internal CreateCommandResult() { }
}