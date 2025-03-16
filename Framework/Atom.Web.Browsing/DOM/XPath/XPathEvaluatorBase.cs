using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

[ScriptUsage(ScriptAccess.None)]
internal static class XPathEvaluatorBase
{
    public static IXPathExpression CreateExpression(string expression, IXPathNSResolver? resolver) => throw new NotImplementedException();

    public static IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type, IXPathResult? result) => throw new NotImplementedException();
}