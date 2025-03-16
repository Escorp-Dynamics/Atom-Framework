using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет перечислитель узла.
/// </summary>
public class NodeIterator : INodeIterator
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode Root { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode ReferenceNode { get; }

    /// <inheritdoc/>
    [ScriptMember("pointerBeforeReferenceNode", ScriptAccess.ReadOnly)]
    public bool IsPointerBeforeReferenceNode { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public FilterShow WhatToShow { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeFilter? Filter { get; }

    internal NodeIterator(INode root, INode referenceNode)
    {
        Root = root;
        ReferenceNode = referenceNode;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Detach() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode? NextNode() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode? PreviousNode() => throw new NotImplementedException();
}