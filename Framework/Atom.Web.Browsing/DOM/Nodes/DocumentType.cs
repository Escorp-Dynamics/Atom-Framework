using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет тип документа.
/// </summary>
public class DocumentType : Node, IDocumentType
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string PublicId { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string SystemId { get; }

    internal DocumentType(Uri namespaceURI, string name, string publicId, string systemId) : base(namespaceURI, name, NodeType.DocumentType)
    {
        PublicId = publicId;
        SystemId = systemId;
    }

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