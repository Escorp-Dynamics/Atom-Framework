using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет итератор дерева DOM.
/// </summary>
public class TreeWalker : ITreeWalker
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? Root { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public FilterShow WhatToShow { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeFilter? Filter { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public INode? CurrentNode { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public INode? FirstChild() => CurrentNode is null ? default : (CurrentNode = CurrentNode.FirstChild);

    /// <inheritdoc/>
    [ScriptMember]
    public INode? LastChild() => CurrentNode is null ? default : (CurrentNode = CurrentNode.LastChild);

    /// <inheritdoc/>
    [ScriptMember]
    public INode? NextNode() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode? NextSibling() => CurrentNode is null ? default : (CurrentNode = CurrentNode.NextSibling);

    /// <inheritdoc/>
    [ScriptMember]
    public INode? ParentNode() => CurrentNode is null || CurrentNode == Root ? default : (CurrentNode = CurrentNode.Parent);

    /// <inheritdoc/>
    [ScriptMember]
    public INode? PreviousNode() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode? PreviousSibling() => CurrentNode is null ? default : (CurrentNode = CurrentNode.PreviousSibling);
}