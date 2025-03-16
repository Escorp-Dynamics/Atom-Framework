using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет информацию о пользовательском контексте для браузера.
/// </summary>
public class UserContextInfo
{
    /// <summary>
    /// Идентификатор пользовательского контекста.
    /// </summary>
    [JsonPropertyName("userContext")]
    [JsonRequired]
    [JsonInclude]
    public string UserContextId { get; internal set; } = string.Empty;
}