using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет доверенный HTML.
/// </summary>
public interface ITrustedHTML
{
    /// <summary>
    /// Преобразует HTML в JSON.
    /// </summary>
    [ScriptMember]
    string ToJSON();
}