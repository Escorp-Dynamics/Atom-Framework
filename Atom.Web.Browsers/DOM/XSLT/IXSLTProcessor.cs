using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public interface IXSLTProcessor
{
    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="style"></param>
    [ScriptMember]
    void ImportStylesheet(INode style);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="output"></param>
    /// <returns></returns>
    [ScriptMember]
    IDocumentFragment TransformToFragment(INode source, IDocument output);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    [ScriptMember]
    IDocument TransformToDocument(INode source);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="namespaceURI"></param>
    /// <param name="localName"></param>
    /// <param name="value"></param>
    [ScriptMember]
    void SetParameter(Uri namespaceURI, string localName, object? value);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="namespaceURI"></param>
    /// <param name="localName"></param>
    /// <returns></returns>
    [ScriptMember]
    object? GetParameter(Uri namespaceURI, string localName);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="namespaceURI"></param>
    /// <param name="localName"></param>
    [ScriptMember]
    void RemoveParameter(Uri namespaceURI, string localName);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void ClearParameters();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Reset();
}