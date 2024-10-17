using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет узел элемента DOM.
/// </summary>
public class Element : Node, IElement
{
    /// <inheritdoc/>
    [ScriptMember("namespaceURI", ScriptAccess.ReadOnly)]
    public new Uri Uri { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? Prefix { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string LocalName { get; }

    /// <inheritdoc/>
    [ScriptMember("tagName", ScriptAccess.ReadOnly)]
    public string Tag { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public string Id { get; set; }

    /// <inheritdoc/>
    [ScriptMember("className")]
    public string Class { get; set; }

    /// <inheritdoc/>
    [ScriptMember("classList", ScriptAccess.ReadOnly)]
    public IDOMTokenList Classes { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public string Slot { get; set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INamedNodeMap Attributes { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IShadowRoot? ShadowRoot { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IHTMLCollection Children { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? FirstElementChild => Children.FirstOrDefault();

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? LastElementChild => Children.LastOrDefault();

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int ChildElementCount => Children.Length;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? PreviousElementSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? NextElementSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IHTMLSlotElement? AssignedSlot { get; }

    /// <inheritdoc/>
    [ScriptMember("getAttributeNames", ScriptAccess.ReadOnly)]
    public IEnumerable<string> AttributeNames { get; } = [];

    internal Element(Uri namespaceURI, string localName, NodeType type) : base(namespaceURI, localName.ToUpperInvariant(), type)
    {
        Uri = namespaceURI;
        LocalName = string.Empty;
        Tag = localName.ToUpperInvariant();
        Id = string.Empty;
        Class = string.Empty;
        Classes = new DOMTokenList();
        Slot = string.Empty;
        Attributes = new NamedNodeMap();
        Children = new HTMLCollection();
    }

    /// <inheritdoc/>
    [ScriptMember]
    public IShadowRoot AttachShadow(ShadowRootInit init) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IElement? Closest(string selectors) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public string? GetAttribute(string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("getAttributeNS")]
    public string? GetAttribute(Uri? namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IAttr? GetAttributeNode(string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("getAttributeNodeNS")]
    public IAttr? GetAttributeNode(Uri? namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("getElementsByClassName")]
    public IHTMLCollection GetElementsByClass(string classNames) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("getElementsByTagName")]
    public IHTMLCollection GetElementsByTag(string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("getElementsByTagNameNS")]
    public IHTMLCollection GetElementsByTag(Uri? namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool HasAttribute(string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("hasAttributeNS")]
    public bool HasAttribute(Uri? namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool HasAttributes() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool Matches(string selectors) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void RemoveAttribute(string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("removeAttributeNS")]
    public void RemoveAttribute(Uri? namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IAttr? RemoveAttributeNode(IAttr attr) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetAttribute(string qualifiedName, string value) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("setAttributeNS")]
    public void SetAttribute(Uri? namespaceURI, string qualifiedName, string value) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IAttr? SetAttributeNode(IAttr attr) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("setAttributeNodeNS")]
    public IAttr? SetAttributeNodeByNS(IAttr attr) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool ToggleAttribute(string qualifiedName, bool force) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool ToggleAttribute(string qualifiedName) => ToggleAttribute(qualifiedName, default);

    /// <inheritdoc/>
    [ScriptMember]
    public void Prepend(params INode[] nodes) => ParentNode.Prepend(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Prepend(params string[] nodes) => ParentNode.Prepend(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Append(params INode[] nodes) => ParentNode.Append(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Append(params string[] nodes) => ParentNode.Append(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceChildren(params INode[] nodes) => ParentNode.ReplaceChildren(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceChildren(params string[] nodes) => ParentNode.ReplaceChildren(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public IElement? QuerySelector(string selectors) => ParentNode.QuerySelector(selectors);

    /// <inheritdoc/>
    [ScriptMember]
    public INodeList QuerySelectorAll(string selectors) => ParentNode.QuerySelectorAll(selectors);

    /// <inheritdoc/>
    [ScriptMember]
    public void Before(params INode[] nodes) => ChildNode.Before(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Before(params string[] nodes) => ChildNode.Before(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void After(params INode[] nodes) => ChildNode.After(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void After(params string[] nodes) => ChildNode.After(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceWith(params INode[] nodes) => ChildNode.ReplaceWith(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceWith(params string[] nodes) => ChildNode.ReplaceWith(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Remove() => ChildNode.Remove();
}