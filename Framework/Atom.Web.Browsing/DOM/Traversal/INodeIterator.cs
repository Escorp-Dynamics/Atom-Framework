using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет перечислитель узла.
/// </summary>
public interface INodeIterator
{
    /// <summary>
    /// Ссылка на корневой узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode Root { get; }

    /// <summary>
    /// Ссылка на референсный узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode ReferenceNode { get; }

    /// <summary>
    /// Указывает, есть ли указатель перед референсным узлом.
    /// </summary>
    [ScriptMember("pointerBeforeReferenceNode", ScriptAccess.ReadOnly)]
    bool IsPointerBeforeReferenceNode { get; }

    /// <summary>
    /// Режим отображения узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    FilterShow WhatToShow { get; }

    /// <summary>
    /// Фильтр узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INodeFilter? Filter { get; }

    /// <summary>
    /// Возвращает ссылку на следующий узел (если он есть).
    /// </summary>
    [ScriptMember]
    INode? NextNode();

    /// <summary>
    /// Возвращает ссылку на предыдущий узел (если он есть).
    /// </summary>
    [ScriptMember]
    INode? PreviousNode();

    /// <summary>
    /// Отсоединяет итератор.
    /// </summary>
    [ScriptMember]
    void Detach();
}