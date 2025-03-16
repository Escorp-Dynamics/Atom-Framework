using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет фильтр узлов.
/// </summary>
public class NodeFilter : INodeFilter
{
    /// <inheritdoc/>
    [ScriptMember]
    public FilterType AcceptNode(INode node) => throw new NotImplementedException();
}