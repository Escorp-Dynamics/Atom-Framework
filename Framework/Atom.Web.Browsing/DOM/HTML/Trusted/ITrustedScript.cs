using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет доверенный скрипт.
/// </summary>
public interface ITrustedScript
{
    /// <summary>
    /// Преобразует скрипт в JSON.
    /// </summary>
    [ScriptMember]
    string ToJSON();
}