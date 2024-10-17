using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public interface IXPathNSResolver
{
    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="prefix">TODO.</param>
    [ScriptMember]
    Uri? LookupNamespaceURI(string? prefix);
}