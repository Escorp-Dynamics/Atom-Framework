using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет результат получения текущих клиентских окон для команды browser.createUserContext.
/// </summary>
public class GetClientWindowsCommandResult : CommandResult
{
    /// <summary>
    /// Список информации о клиентских окнах текущего браузера для целей сериализации.
    /// </summary>
    [JsonPropertyName("clientWindows")]
    [JsonRequired]
    [JsonInclude]
    internal IList<ClientWindowInfo> SerializableClientWindows { get; set; } = [];

    /// <summary>
    /// Коллекция информации обо всех клиентских окнах, открытых для текущего браузера.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ClientWindowInfo> ClientWindows => SerializableClientWindows.AsReadOnly();
}