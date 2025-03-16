using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.getClientWindows.
/// </summary>
public class GetClientWindowsCommandParameters : CommandParameters<GetClientWindowsCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.getClientWindows";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GetClientWindowsCommandParameters"/>.
    /// </summary>
    public GetClientWindowsCommandParameters() { }
}