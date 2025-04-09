using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет интерфейс для реализации родительского узла.
/// </summary>
public interface IParentNode
{
    /// <summary>
    /// Коллекция дочерних элементов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IHTMLCollection Children { get; }

    /// <summary>
    /// Ссылка на первый дочерний элемент.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? FirstElementChild { get; }

    /// <summary>
    /// Ссылка на последний дочерний элемент.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement? LastElementChild { get; }

    /// <summary>
    /// Количество дочерних элементов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int ChildElementCount { get; }

    /// <summary>
    /// Добавляет узлы в начало коллекции дочерних элементов.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Prepend(params IEnumerable<INode> nodes);

    /// <summary>
    /// Добавляет узлы в начало коллекции дочерних элементов.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Prepend(params IEnumerable<string> nodes);

    /// <summary>
    /// Добавляет узлы в конец коллекции дочерних элементов.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Append(params IEnumerable<INode> nodes);

    /// <summary>
    /// Добавляет узлы в конец коллекции дочерних элементов.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Append(params IEnumerable<string> nodes);

    /// <summary>
    /// Заменяет коллекцию дочерних элементов указанными узлами.
    /// </summary>
    /// <param name="nodes">Новые узлы.</param>
    [ScriptMember]
    void ReplaceChildren(params IEnumerable<INode> nodes);

    /// <summary>
    /// Заменяет коллекцию дочерних элементов указанными узлами.
    /// </summary>
    /// <param name="nodes">Новые узлы.</param>
    [ScriptMember]
    void ReplaceChildren(params IEnumerable<string> nodes);

    /// <summary>
    /// Выбирает первый элемент, соответствующий селектору поиска.
    /// </summary>
    /// <param name="selectors">Селектор поиска элемента.</param>
    [ScriptMember]
    IElement? QuerySelector(string selectors);

    /// <summary>
    /// Выбирает список узлов, соответствующий селектору поиска.
    /// </summary>
    /// <param name="selectors">Селектор поиска узлов.</param>
    [ScriptMember]
    INodeList QuerySelectorAll(string selectors);
}