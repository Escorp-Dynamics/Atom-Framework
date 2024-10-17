using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет результат XPath.
/// </summary>
[ScriptUsage]
public interface IXPathResult
{
    /// <summary>
    /// Любой.
    /// </summary>
    [ScriptMember("ANY_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType AnyType = XPathResultType.Any;

    /// <summary>
    /// Число.
    /// </summary>
    [ScriptMember("NUMBER_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType NumberType = XPathResultType.Number;

    /// <summary>
    /// Строка.
    /// </summary>
    [ScriptMember("STRING_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType StringType = XPathResultType.String;

    /// <summary>
    /// Логическое значение.
    /// </summary>
    [ScriptMember("BOOLEAN_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType BooleanType = XPathResultType.Boolean;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("UNORDERED_NODE_ITERATOR_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType UnorderedNodeIteratorType = XPathResultType.UnorderedNodeIterator;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("ORDERED_NODE_ITERATOR_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType OrderedNodeIteratorType = XPathResultType.OrderedNodeIterator;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("UNORDERED_NODE_SNAPSHOT_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType UnorderedNodeSnapshotType = XPathResultType.UnorderedNodeSnapshot;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("ORDERED_NODE_SNAPSHOT_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType OrderedNodeSnapshotType = XPathResultType.OrderedNodeSnapshot;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("ANY_UNORDERED_NODE_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType AnyUnorderedNodeType = XPathResultType.AnyUnorderedNode;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("FIRST_ORDERED_NODE_TYPE", ScriptAccess.ReadOnly)]
    public const XPathResultType FirstOrderedNodeType = XPathResultType.FirstOrderedNode;

    /// <summary>
    /// Тип результата XPath.
    /// </summary>
    [ScriptMember("resultType", ScriptAccess.ReadOnly)]
    XPathResultType Type { get; }

    /// <summary>
    /// Числовое значение.
    /// </summary>
    [ScriptMember]
    public double NumberValue { get; }

    /// <summary>
    /// Строковое значение.
    /// </summary>
    [ScriptMember]
    public string? StringValue { get; }

    /// <summary>
    /// Логическое значение.
    /// </summary>
    [ScriptMember]
    public bool BooleanValue { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    public INode? SingleNodeValue { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    public bool InvalidIteratorState { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    public int SnapshotLength { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    INode? IterateNext();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    INode? SnapshotItem(int index);
}