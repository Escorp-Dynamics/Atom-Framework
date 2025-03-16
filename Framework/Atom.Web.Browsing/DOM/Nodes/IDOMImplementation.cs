using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет имплементацию DOM.
/// </summary>
public interface IDOMImplementation
{
    /// <summary>
    /// Создаёт узел типа документа.
    /// </summary>
    /// <param name="qualifiedName">Имя узла.</param>
    /// <param name="publicId">Публичный идентификатор.</param>
    /// <param name="systemId">Системный идентификатор.</param>
    [ScriptMember]
    IDocumentType CreateDocumentType(string qualifiedName, string publicId, string systemId);

    /// <summary>
    /// Создаёт узел документа в схеме XML.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="qualifiedName">Имя узла.</param>
    /// <param name="doctype">Тип документа.</param>
    [ScriptMember]
    IXMLDocument CreateDocument(Uri namespaceURI, string qualifiedName, IDocumentType doctype);

    /// <summary>
    /// Создаёт узел документа в схеме XML.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="qualifiedName">Имя узла.</param>
    [ScriptMember]
    IXMLDocument CreateDocument(Uri namespaceURI, string qualifiedName);

    /// <summary>
    /// Создаёт узел документа HTML.
    /// </summary>
    /// <param name="title">Заголовок документа.</param>
    [ScriptMember]
    IDocument CreateHTMLDocument(string title);

    /// <summary>
    /// Создаёт узел документа HTML.
    /// </summary>
    [ScriptMember]
    IDocument CreateHTMLDocument() => CreateHTMLDocument(string.Empty);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    bool HasFeature();
}