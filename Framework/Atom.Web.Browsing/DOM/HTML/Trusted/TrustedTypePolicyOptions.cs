using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Делегат создания доверенного HTML.
/// </summary>
/// <param name="input">Входные данные.</param>
/// <param name="arguments">Аргументы.</param>
public delegate string? CreateHTMLCallback(string input, params IEnumerable<object?> arguments);

/// <summary>
/// Делегат создания доверенного скрипта.
/// </summary>
/// <param name="input">Входные данные.</param>
/// <param name="arguments">Аргументы.</param>
public delegate string? CreateScriptCallback(string input, params IEnumerable<object?> arguments);

/// <summary>
/// Делегат создания доверенного скрипта в формате ссылки.
/// </summary>
/// <param name="input">Входные данные.</param>
/// <param name="arguments">Аргументы.</param>
public delegate Uri? CreateScriptURLCallback(string input, params IEnumerable<object?> arguments);

/// <summary>
/// Представляет настройки политики доверия.
/// </summary>
public class TrustedTypePolicyOptions
{
    /// <summary>
    /// Делегат создания доверенного HTML.
    /// </summary>
    [ScriptMember]
    public CreateHTMLCallback? CreateHTML { get; set; }

    /// <summary>
    /// Делегат создания доверенного скрипта.
    /// </summary>
    [ScriptMember]
    public CreateScriptCallback? CreateScript { get; set; }

    /// <summary>
    /// Делегат создания доверенного скрипта в формате ссылки.
    /// </summary>
    [ScriptMember]
    public CreateScriptURLCallback? CreateScriptURL { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static TrustedTypePolicyOptions Default { get; } = new();
}