using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public class XPathNSResolver : IXPathNSResolver
{
    /// <inheritdoc/>
    [ScriptMember]
    public Uri? LookupNamespaceURI(string? prefix) => throw new NotImplementedException();
}