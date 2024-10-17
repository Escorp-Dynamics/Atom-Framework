using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет фильтр узлов.
/// </summary>
public class NodeFilter : INodeFilter
{
    /// <inheritdoc/>
    [ScriptMember]
    public FilterType AcceptNode(INode node) => throw new NotImplementedException();
}