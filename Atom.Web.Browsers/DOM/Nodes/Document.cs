using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет документ DOM.
/// </summary>
public class Document : Node, IDocument
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IDOMImplementation Implementation { get; }

    /// <inheritdoc/>
    [ScriptMember("URL", ScriptAccess.ReadOnly)]
    public Uri Url { get; }

    /// <inheritdoc/>
    [ScriptMember("documentURI", ScriptAccess.ReadOnly)]
    public new Uri Uri { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string CompatMode { get; }

    /// <inheritdoc/>
    [ScriptMember("characterSet", ScriptAccess.ReadOnly)]
    public string CharSet { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string ContentType { get; }

    /// <inheritdoc/>
    [ScriptMember("doctype", ScriptAccess.ReadOnly)]
    public IDocumentType? DocType { get; internal set; }

    /// <inheritdoc/>
    [ScriptMember("documentElement", ScriptAccess.ReadOnly)]
    public IElement? Element { get; internal set; }

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

    internal Document(Uri url, string charSet, string contentType) : base(url, "#document", NodeType.Document)
    {
        Implementation = new DOMImplementation();
        Url = url;
        Uri = url;
        CompatMode = "CSS1Compat";
        CharSet = charSet;
        ContentType = contentType;
        Children = new HTMLCollection();
    }

    internal Document() : this(new Uri("about:blank"), "UTF-8", "text/html") { }

    /// <inheritdoc/>
    [ScriptMember]
    public INode AdoptNode(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IAttr CreateAttribute(string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("createAttributeNS")]
    public IAttr CreateAttribute(Uri? namespaceURI, string qualifiedName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public ICDATASection CreateCDATASection(string data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IComment CreateComment(string data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IDocumentFragment CreateDocumentFragment() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IElement CreateElement(string localName, ElementCreationOptions options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IElement CreateElement(string localName, string value) => CreateElement(localName, new ElementCreationOptions { Is = value });

    /// <inheritdoc/>
    [ScriptMember]
    public IElement CreateElement(string localName) => CreateElement(localName, new ElementCreationOptions());

    /// <inheritdoc/>
    [ScriptMember("createElementNS")]
    public IElement CreateElement(Uri? namespaceURI, string qualifiedName, ElementCreationOptions options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("createElementNS")]
    public IElement CreateElement(Uri? namespaceURI, string localName, string value) => CreateElement(namespaceURI, localName, new ElementCreationOptions { Is = value });

    /// <inheritdoc/>
    [ScriptMember("createElementNS")]
    public IElement CreateElement(Uri? namespaceURI, string localName) => CreateElement(namespaceURI, localName, new ElementCreationOptions());

    /// <inheritdoc/>
    [ScriptMember]
    public INodeIterator CreateNodeIterator(INode root, FilterShow whatToShow, INodeFilter? filter) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INodeIterator CreateNodeIterator(INode root, FilterShow whatToShow) => CreateNodeIterator(root, whatToShow, default);

    /// <inheritdoc/>
    [ScriptMember]
    public INodeIterator CreateNodeIterator(INode root) => CreateNodeIterator(root, FilterShow.All);

    /// <inheritdoc/>
    [ScriptMember]
    public IProcessingInstruction CreateProcessingInstruction(string target, string data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IRange CreateRange() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IText CreateTextNode(string data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public ITreeWalker CreateTreeWalker(INode root, FilterShow whatToShow, INodeFilter? filter) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public ITreeWalker CreateTreeWalker(INode root, FilterShow whatToShow) => CreateTreeWalker(root, whatToShow, default);

    /// <inheritdoc/>
    [ScriptMember]
    public ITreeWalker CreateTreeWalker(INode root) => CreateTreeWalker(root, FilterShow.All);

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
    public INode ImportNode(INode node, bool deep) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode ImportNode(INode node) => ImportNode(node, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IElement? GetElementById(string id) => NonElementParentNode.GetElementById(id);

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
    public IXPathExpression CreateExpression(string expression, IXPathNSResolver? resolver) => XPathEvaluatorBase.CreateExpression(expression, resolver);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathExpression CreateExpression(string expression) => CreateExpression(expression, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type, IXPathResult? result) => XPathEvaluatorBase.Evaluate(expression, contextNode, resolver, type, result);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type) => Evaluate(expression, contextNode, resolver, type, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver) => Evaluate(expression, contextNode, resolver, XPathResultType.Any);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode) => Evaluate(expression, contextNode, default);
}