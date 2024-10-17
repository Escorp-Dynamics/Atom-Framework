using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

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
}