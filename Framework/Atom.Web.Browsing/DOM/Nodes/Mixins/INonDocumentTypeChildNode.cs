using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет интерфейс для реализации не-doctype дочернего узла.
/// </summary>
public interface INonDocumentTypeChildNode
{
    /// <summary>
    /// Предыдущий элемент.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? PreviousElementSibling { get; }

    /// <summary>
    /// Следующий элемент.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? NextElementSibling { get; }
}