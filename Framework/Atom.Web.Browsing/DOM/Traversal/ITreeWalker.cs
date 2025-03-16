using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет итератор дерева DOM.
/// </summary>
public interface ITreeWalker
{
    /// <summary>
    /// Ссылка на корневой узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode? Root { get; }

    /// <summary>
    /// Фильтр отображения узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    FilterShow WhatToShow { get; }

    /// <summary>
    /// Фильтр узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INodeFilter? Filter { get; }

    /// <summary>
    /// Ссылка на текущий узел.
    /// </summary>
    [ScriptMember]
    INode? CurrentNode { get; set; }

    /// <summary>
    /// Возвращает ссылку на родительский узел.
    /// </summary>
    [ScriptMember]
    INode? ParentNode();

    /// <summary>
    /// Возвращает ссылку на первый дочерний узел.
    /// </summary>
    [ScriptMember]
    INode? FirstChild();

    /// <summary>
    /// Возвращает ссылку на последний дочерний узел.
    /// </summary>
    [ScriptMember]
    INode? LastChild();

    /// <summary>
    /// Возвращает ссылку на предыдущий узел дерева.
    /// </summary>
    [ScriptMember]
    INode? PreviousSibling();

    /// <summary>
    /// Возвращает ссылку на следующий узел дерева.
    /// </summary>
    [ScriptMember]
    INode? NextSibling();

    /// <summary>
    /// Возвращает ссылку на предыдущий узел.
    /// </summary>
    [ScriptMember]
    INode? PreviousNode();

    /// <summary>
    /// Возвращает ссылку на следующий узел.
    /// </summary>
    [ScriptMember]
    INode? NextNode();
}