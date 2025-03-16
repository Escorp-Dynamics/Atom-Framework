using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

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