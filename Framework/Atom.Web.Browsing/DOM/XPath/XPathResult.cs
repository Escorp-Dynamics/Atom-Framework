using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет результат XPath.
/// </summary>
[ScriptUsage]
public class XPathResult : IXPathResult
{
    /// <inheritdoc/>
    [ScriptMember("resultType", ScriptAccess.ReadOnly)]
    public XPathResultType Type { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public double NumberValue { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public string? StringValue { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public bool BooleanValue { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public INode? SingleNodeValue { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public bool InvalidIteratorState { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public int SnapshotLength { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public INode? IterateNext() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public INode? SnapshotItem(int index) => throw new NotImplementedException();
}