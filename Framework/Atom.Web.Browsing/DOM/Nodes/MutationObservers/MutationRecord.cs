using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет запись мутации.
/// </summary>
public class MutationRecord : IMutationRecord
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string Type { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode Target { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeList AddedNodes { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeList RemovedNodes { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? PreviousSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? NextSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? AttributeName { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? AttributeNamespace { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? OldValue { get; }

    internal MutationRecord(string type, INode target)
    {
        Type = type;
        Target = target;
        AddedNodes = new NodeList();
        RemovedNodes = new NodeList();
    }
}