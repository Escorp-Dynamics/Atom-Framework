using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет доверенный скрипт в формате ссылки.
/// </summary>
public interface ITrustedScriptURL
{
    /// <summary>
    /// Преобразует скрипт в JSON.
    /// </summary>
    [ScriptMember]
    string ToJSON();
}