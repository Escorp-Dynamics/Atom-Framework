using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет политику доверия.
/// </summary>
public interface ITrustedTypePolicy
{
    /// <summary>
    /// Название политики.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string Name { get; }

    /// <summary>
    /// Создаёт доверительный HTML.
    /// </summary>
    /// <param name="input">Входные данные.</param>
    /// <param name="arguments">Аргументы.</param>
    [ScriptMember]
    ITrustedHTML CreateHTML(string input, params object?[] arguments);

    /// <summary>
    /// Создаёт доверительный скрипт.
    /// </summary>
    /// <param name="input">Входные данные.</param>
    /// <param name="arguments">Аргументы.</param>
    [ScriptMember]
    ITrustedScript CreateScript(string input, params object?[] arguments);

    /// <summary>
    /// Создаёт доверительный скрипт в формате ссылки.
    /// </summary>
    /// <param name="input">Входные данные.</param>
    /// <param name="arguments">Аргументы.</param>
    [ScriptMember]
    ITrustedScriptURL CreateScriptURL(string input, params object?[] arguments);
}