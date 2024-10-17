using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Тип результата XPath.
/// </summary>
[ScriptUsage]
public enum XPathResultType : ushort
{
    /// <summary>
    /// Любой.
    /// </summary>
    Any,
    /// <summary>
    /// Число.
    /// </summary>
    Number,
    /// <summary>
    /// Строка.
    /// </summary>
    String,
    /// <summary>
    /// Логическое значение.
    /// </summary>
    Boolean,
    /// <summary>
    /// TODO.
    /// </summary>
    UnorderedNodeIterator,
    /// <summary>
    /// TODO.
    /// </summary>
    OrderedNodeIterator,
    /// <summary>
    /// TODO.
    /// </summary>
    UnorderedNodeSnapshot,
    /// <summary>
    /// TODO.
    /// </summary>
    OrderedNodeSnapshot,
    /// <summary>
    /// TODO.
    /// </summary>
    AnyUnorderedNode,
    /// <summary>
    /// TODO.
    /// </summary>
    FirstOrderedNode,
}