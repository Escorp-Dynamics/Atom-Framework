using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public class XSLTProcessor : IXSLTProcessor
{
    /// <inheritdoc/>
    [ScriptMember]
    public void ClearParameters() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public object? GetParameter(Uri namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void ImportStylesheet(INode style) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void RemoveParameter(Uri namespaceURI, string localName) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Reset() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetParameter(Uri namespaceURI, string localName, object? value) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IDocument TransformToDocument(INode source) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IDocumentFragment TransformToFragment(INode source, IDocument output) => throw new NotImplementedException();
}