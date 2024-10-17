using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет запись мутации.
/// </summary>
public interface IMutationRecord
{
    /// <summary>
    /// Тип мутации.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string Type { get; }

    /// <summary>
    /// Цель мутации.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode Target { get; }

    /// <summary>
    /// Список добавленных узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeList AddedNodes { get; }

    /// <summary>
    /// Список удалённых узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeList RemovedNodes { get; }

    /// <summary>
    /// Предыдущий узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? PreviousSibling { get; }

    /// <summary>
    /// Следующий узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? NextSibling { get; }

    /// <summary>
    /// Название атрибута.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? AttributeName { get; }

    /// <summary>
    /// Пространство имён атрибута.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? AttributeNamespace { get; }

    /// <summary>
    /// Предыдущее значение.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? OldValue { get; }
}