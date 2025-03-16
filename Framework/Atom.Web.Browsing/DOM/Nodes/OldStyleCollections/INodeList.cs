using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет список узлов.
/// </summary>
public interface INodeList : IEnumerable<INode>
{
    /// <summary>
    /// Возвращает узел по его индексу.
    /// </summary>
    [ScriptMember]
    INode? this[int index] { get; }

    /// <summary>
    /// Количество узлов в списке.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int Length { get; }

    /// <summary>
    /// Добавляет узел в список.
    /// </summary>
    /// <param name="node">Добавляемый узел.</param>
    [ScriptMember(ScriptAccess.None)]
    void Add(INode node);
}