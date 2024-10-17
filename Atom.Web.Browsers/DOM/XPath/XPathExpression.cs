using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет выражение XPath.
/// </summary>
[ScriptUsage]
public class XPathExpression : IXPathExpression
{
    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(INode contextNode, XPathResultType type, IXPathResult? result) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(INode contextNode, XPathResultType type) => Evaluate(contextNode, type, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(INode contextNode) => Evaluate(contextNode, XPathResultType.Any);
}