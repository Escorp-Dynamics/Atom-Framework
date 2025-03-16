namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет элемент head в документе HTML.
/// </summary>
public class HTMLHeadElement : HTMLElement, IHTMLHeadElement
{
    internal HTMLHeadElement(Uri namespaceURI) : base(namespaceURI, "head") { }
}