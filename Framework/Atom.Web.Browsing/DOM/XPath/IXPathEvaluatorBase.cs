using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public interface IXPathEvaluatorBase
{
    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="resolver"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathExpression CreateExpression(string expression, IXPathNSResolver? resolver);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathExpression CreateExpression(string expression) => CreateExpression(expression, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="contextNode"></param>
    /// <param name="resolver"></param>
    /// <param name="type"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type, IXPathResult? result);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="contextNode"></param>
    /// <param name="resolver"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type) => Evaluate(expression, contextNode, resolver, type, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="contextNode"></param>
    /// <param name="resolver"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver) => Evaluate(expression, contextNode, resolver, XPathResultType.Any);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="contextNode"></param>
    /// <returns></returns>
    [ScriptMember]
    IXPathResult Evaluate(string expression, INode contextNode) => Evaluate(expression, contextNode, default);
}