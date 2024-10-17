using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

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