using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет узел дерева DOM.
/// </summary>
public class Node : INode
{
    /// <inheritdoc/>
    [ScriptMember("nodeType", ScriptAccess.ReadOnly)]
    public NodeType Type { get; }

    /// <inheritdoc/>
    [ScriptMember("nodeName", ScriptAccess.ReadOnly)]
    public string Name { get; }

    /// <inheritdoc/>
    [ScriptMember("baseURI", ScriptAccess.ReadOnly)]
    public Uri Uri { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public bool IsConnected { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IDocument? OwnerDocument { get; }

    /// <inheritdoc/>
    [ScriptMember("parentNode", ScriptAccess.ReadOnly)]
    public INode? Parent { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? ParentElement { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INodeList ChildNodes { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? FirstChild { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? LastChild { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? PreviousSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode? NextSibling { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public string? NodeValue { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public string? TextContent { get; set; }

    internal Node(Uri baseURI, string name, NodeType type)
    {
        Uri = baseURI;
        Name = name;
        Type = type;
        ChildNodes = new NodeList();
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, IEventListener callback, AddEventListenerOptions options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode AppendChild(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode CloneNode(bool deep) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public DocumentPosition CompareDocumentPosition(INode other) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool Contains(INode? other) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool DispatchEvent(IEvent @event) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode GetRootNode(GetRootNodeOptions options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool HasChildNodes() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode InsertBefore(INode node, INode? child) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsDefaultNamespace(Uri? namespaceUri) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsEqualNode(INode? otherNode) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsSameNode(INode? otherNode) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public Uri? LookupNamespaceURI(string? prefix) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public string? LookupPrefix(Uri? namespaceUri) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Normalize() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode RemoveChild(INode child) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void RemoveEventListener(string type, IEventListener callback, EventListenerOptions options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode ReplaceChild(INode node, INode child) => throw new NotImplementedException();
}