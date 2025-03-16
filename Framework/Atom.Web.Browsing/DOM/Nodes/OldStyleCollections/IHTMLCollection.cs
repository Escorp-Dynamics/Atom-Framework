using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет именованную коллекцию элементов HTML.
/// </summary>
public interface IHTMLCollection : IEnumerable<IElement?>
{
    /// <summary>
    /// Количество элементов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Возвращает элемент по его индексу.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? this[int index] { get; }

    /// <summary>
    /// Возвращает элемент по его имени.
    /// </summary>
    /// <param name="name">Имя элемента.</param>
    [ScriptMember]
    IElement? NamedItem(string name);
}