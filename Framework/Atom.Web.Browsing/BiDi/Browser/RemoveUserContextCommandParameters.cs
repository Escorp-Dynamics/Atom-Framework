using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.removeUserContext.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="RemoveUserContextCommandParameters"/>.
/// </remarks>
/// <param name="userContextId">Идентификатор пользовательского контекста для удаления.</param>
public class RemoveUserContextCommandParameters(string userContextId) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.removeUserContext";

    /// <summary>
    /// Идентификатор пользовательского контекста для удаления.
    /// </summary>
    [JsonPropertyName("userContext")]
    public string UserContextId { get; set; } = userContextId;
}