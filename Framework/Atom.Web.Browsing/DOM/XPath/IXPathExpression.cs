using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет выражение XPath.
/// </summary>
[ScriptUsage]
public interface IXPathExpression
{
    /// <summary>
    /// Вычисляет результат выражения XPath.
    /// </summary>
    /// <param name="contextNode">Узел контекста.</param>
    /// <param name="type">Тип результата.</param>
    /// <param name="result">Предыдущий результат.</param>
    [ScriptMember]
    IXPathResult Evaluate(INode contextNode, XPathResultType type, IXPathResult? result);

    /// <summary>
    /// Вычисляет результат выражения XPath.
    /// </summary>
    /// <param name="contextNode">Узел контекста.</param>
    /// <param name="type">Тип результата.</param>
    [ScriptMember]
    IXPathResult Evaluate(INode contextNode, XPathResultType type) => Evaluate(contextNode, type, default);

    /// <summary>
    /// Вычисляет результат выражения XPath.
    /// </summary>
    /// <param name="contextNode">Узел контекста.</param>
    [ScriptMember]
    IXPathResult Evaluate(INode contextNode) => Evaluate(contextNode, XPathResultType.Any);
}