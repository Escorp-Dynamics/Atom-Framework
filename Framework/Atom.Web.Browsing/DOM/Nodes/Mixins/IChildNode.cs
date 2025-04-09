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
    void Before(params IEnumerable<INode> nodes);

    /// <summary>
    /// Добавляет узлы перед текущим узлом.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void Before(params IEnumerable<string> nodes);

    /// <summary>
    /// Добавляет узлы после текущего узла.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void After(params IEnumerable<INode> nodes);

    /// <summary>
    /// Добавляет узлы после текущего узла.
    /// </summary>
    /// <param name="nodes">Добавляемые узлы.</param>
    [ScriptMember]
    void After(params IEnumerable<string> nodes);

    /// <summary>
    /// Заменяет текущий узел новыми узлами.
    /// </summary>
    /// <param name="nodes">Заменяемые узлы.</param>
    [ScriptMember]
    void ReplaceWith(params IEnumerable<INode> nodes);

    /// <summary>
    /// Заменяет текущий узел новыми узлами.
    /// </summary>
    /// <param name="nodes">Заменяемые узлы.</param>
    [ScriptMember]
    void ReplaceWith(params IEnumerable<string> nodes);

    /// <summary>
    /// Удаляет текущий узел.
    /// </summary>
    [ScriptMember]
    void Remove();
}