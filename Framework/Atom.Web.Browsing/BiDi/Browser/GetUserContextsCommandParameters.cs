using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.getUserContexts.
/// </summary>
public class GetUserContextsCommandParameters : CommandParameters<GetUserContextsCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.getUserContexts";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GetUserContextsCommandParameters"/>.
    /// </summary>
    public GetUserContextsCommandParameters() { }
}