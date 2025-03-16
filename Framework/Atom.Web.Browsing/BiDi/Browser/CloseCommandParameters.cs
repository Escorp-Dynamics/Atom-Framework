using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.close.
/// </summary>
public class CloseCommandParameters : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.close";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CloseCommandParameters"/>.
    /// </summary>
    public CloseCommandParameters() { }
}