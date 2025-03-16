using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет результат создания нового пользовательского контекста для команды browser.createUserContext.
/// </summary>
public class CreateUserContextCommandResult : CommandResult
{
    /// <summary>
    /// Идентификатор пользовательского контекста.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonRequired]
    [JsonInclude]
    public string UserContextId { get; internal set; } = string.Empty;
}