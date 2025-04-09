using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет фрагмент документа.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="DocumentFragment"/>.
/// </remarks>
public class DocumentFragment(Uri baseURI, string name) : Node(baseURI, name, NodeType.DocumentFragment), IDocumentFragment
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IHTMLCollection Children { get; } = new HTMLCollection();

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
    [ScriptMember]
    public IElement? GetElementById(string id) => NonElementParentNode.GetElementById(id);

    /// <inheritdoc/>
    [ScriptMember]
    public void Prepend(params IEnumerable<INode> nodes) => ParentNode.Prepend(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Prepend(params IEnumerable<string> nodes) => ParentNode.Prepend(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Append(params IEnumerable<INode> nodes) => ParentNode.Append(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Append(params IEnumerable<string> nodes) => ParentNode.Append(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceChildren(params IEnumerable<INode> nodes) => ParentNode.ReplaceChildren(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceChildren(params IEnumerable<string> nodes) => ParentNode.ReplaceChildren(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public IElement? QuerySelector(string selectors) => ParentNode.QuerySelector(selectors);

    /// <inheritdoc/>
    [ScriptMember]
    public INodeList QuerySelectorAll(string selectors) => ParentNode.QuerySelectorAll(selectors);
}