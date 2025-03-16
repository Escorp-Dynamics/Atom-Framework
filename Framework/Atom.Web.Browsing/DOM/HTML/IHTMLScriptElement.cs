using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет элемент скрипта.
/// </summary>
public interface IHTMLScriptElement
{
    /// <summary>
    /// Внутренний текст.
    /// </summary>
    [ScriptMember]
    string InnerText { get; set; }

    /// <summary>
    /// Содержимое элемента.
    /// </summary>
    [ScriptMember("textContent")]
    string? Content { get; set; }

    /// <summary>
    /// Ссылка на скрипт.
    /// </summary>
    [ScriptMember("src")]
    Uri Source { get; set; }

    /// <summary>
    /// Текстовое представление элемента.
    /// </summary>
    [ScriptMember]
    string Text { get; set; }
}