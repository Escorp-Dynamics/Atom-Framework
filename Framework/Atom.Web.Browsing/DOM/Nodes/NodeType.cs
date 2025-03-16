namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Тип узла.
/// </summary>
public enum NodeType : ushort
{
    /// <summary>
    /// Неизвестно.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// <see cref="IElement"/>.
    /// </summary>
    Element = 1,
    /// <summary>
    /// <see cref="IAttr"/>.
    /// </summary>
    Attribute = 2,
    /// <summary>
    /// <see cref="IText"/>.
    /// </summary>
    Text = 3,
    /// <summary>
    /// <see cref="ICDATASection"/>.
    /// </summary>
    CDATASection = 4,
    /// <summary>
    /// <see cref="IProcessingInstruction"/>.
    /// </summary>
    ProcessingInstruction = 7,
    /// <summary>
    /// <see cref="IComment"/>.
    /// </summary>
    Comment = 8,
    /// <summary>
    /// <see cref="IDocument"/>.
    /// </summary>
    Document = 9,
    /// <summary>
    /// <see cref="IDocumentType"/>.
    /// </summary>
    DocumentType = 10,
    /// <summary>
    /// <see cref="IDocumentFragment"/>.
    /// </summary>
    DocumentFragment = 11,
}