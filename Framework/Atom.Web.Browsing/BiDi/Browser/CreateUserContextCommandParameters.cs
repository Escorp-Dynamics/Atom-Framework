using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.createUserContext.
/// </summary>
public class CreateUserContextCommandParameters : CommandParameters<CreateUserContextCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.createUserContext";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CreateUserContextCommandParameters"/>.
    /// </summary>
    public CreateUserContextCommandParameters() { }
}