using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет базовый интерфейс для реализации дочернего узла.
/// </summary>
public interface IChildNode
{
    /// <summary>
    /// Добавляет узлы перед текущим узлом.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Before(params INode[] nodes);

    /// <summary>
    /// Добавляет узлы перед текущим узлом.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Before(params string[] nodes);

    /// <summary>
    /// Добавляет узлы после текущего узла.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void After(params INode[] nodes);

    /// <summary>
    /// Добавляет узлы после текущего узла.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void After(params string[] nodes);

    /// <summary>
    /// Заменяет текущий узел новыми узлами.
    /// </summary>
    /// <param name="nodes">Заменяемые узлы.</param>
    [ScriptMember]
    void ReplaceWith(params INode[] nodes);

    /// <summary>
    /// Заменяет текущий узел новыми узлами.
    /// </summary>
    /// <param name="nodes">Заменяемые узлы.</param>
    [ScriptMember]
    void ReplaceWith(params string[] nodes);

    /// <summary>
    /// Удаляет текущий узел.
    /// </summary>
    [ScriptMember]
    void Remove();
}