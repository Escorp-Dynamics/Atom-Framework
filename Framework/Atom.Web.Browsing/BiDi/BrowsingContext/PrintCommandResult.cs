using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет результат получения дерева контекстов просмотра с использованием команды browserContext.getTree.
/// </summary>
public class PrintCommandResult : CommandResult
{
    /// <summary>
    /// Данные изображения скриншота в виде строки, закодированной в base64.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Data { get; internal set; } = string.Empty;

    [JsonConstructor]
    internal PrintCommandResult() { }
}