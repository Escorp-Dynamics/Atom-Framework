using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет интерфейс для реализации методов не-родительских узлов.
/// </summary>
public interface INonElementParentNode
{
    /// <summary>
    /// Возвращает элемент по его идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор элемента.</param>
    [ScriptMember]
    IElement? GetElementById(string id);
}