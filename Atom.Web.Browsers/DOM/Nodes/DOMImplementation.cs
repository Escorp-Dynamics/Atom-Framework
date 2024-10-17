using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет имплементацию DOM.
/// </summary>
public class DOMImplementation : IDOMImplementation
{
    /// <inheritdoc/>
    [ScriptMember]
    public IDocumentType CreateDocumentType(string qualifiedName, string publicId, string systemId)
        => new DocumentType(new Uri("about:blank"), qualifiedName, publicId, systemId);

    /// <inheritdoc/>
    [ScriptMember]
    public IXMLDocument CreateDocument(Uri namespaceURI, [NotNull] string qualifiedName, IDocumentType? doctype) => new XMLDocument
    {
        DocType = doctype,
        Element = new Element(namespaceURI ?? new Uri("about:blank"), qualifiedName, NodeType.Document),
    };

    /// <inheritdoc/>
    [ScriptMember]
    public IXMLDocument CreateDocument(Uri namespaceURI, string qualifiedName) => CreateDocument(namespaceURI, qualifiedName, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IDocument CreateHTMLDocument(string title)
    {
        var document = new Document
        {
            DocType = CreateDocumentType("html", string.Empty, string.Empty),
            Element = new Element(new Uri("about:blank"), "html", NodeType.Document),
        };

        var head = document.CreateElement("head");
        var titleElement = document.CreateElement("title");
        titleElement.TextContent = title;
        head.AppendChild(titleElement);
        document.Element!.AppendChild(head);

        var body = document.CreateElement("body");
        document.Element.AppendChild(body);

        return document;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public IDocument CreateHTMLDocument() => CreateHTMLDocument(string.Empty);

    /// <inheritdoc/>
    [ScriptMember]
    public bool HasFeature() => true;
}