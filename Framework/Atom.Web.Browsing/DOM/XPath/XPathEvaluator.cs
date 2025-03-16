using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage]
public class XPathEvaluator : IXPathEvaluator
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="XPathEvaluator"/>.
    /// </summary>
    public XPathEvaluator() { }

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathExpression CreateExpression(string expression, IXPathNSResolver? resolver) => XPathEvaluatorBase.CreateExpression(expression, resolver);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathExpression CreateExpression(string expression) => CreateExpression(expression, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type, IXPathResult? result) => XPathEvaluatorBase.Evaluate(expression, contextNode, resolver, type, result);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver, XPathResultType type) => Evaluate(expression, contextNode, resolver, type, default);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode, IXPathNSResolver? resolver) => Evaluate(expression, contextNode, resolver, XPathResultType.Any);

    /// <inheritdoc/>
    [ScriptMember]
    public IXPathResult Evaluate(string expression, INode contextNode) => Evaluate(expression, contextNode, default);
}