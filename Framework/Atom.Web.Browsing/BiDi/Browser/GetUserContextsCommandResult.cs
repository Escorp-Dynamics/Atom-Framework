using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет информацию о пользовательском контексте для браузера.
/// </summary>
public class GetUserContextsCommandResult : CommandResult
{
    [JsonPropertyName("userContexts")]
    [JsonRequired]
    [JsonInclude]
    internal IList<UserContextInfo> SerializableUserContexts { get; set; } = [];

    /// <summary>
    /// Коллекция всех пользовательских контекстов, открытых для текущего браузера.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<UserContextInfo> UserContexts => SerializableUserContexts.AsReadOnly();
}