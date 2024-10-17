namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет элемент head в документе HTML.
/// </summary>
public class HTMLHeadElement : HTMLElement, IHTMLHeadElement
{
    internal HTMLHeadElement(Uri namespaceURI) : base(namespaceURI, "head") { }
}