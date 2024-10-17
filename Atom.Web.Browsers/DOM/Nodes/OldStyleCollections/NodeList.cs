using System.Collections;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет список узлов.
/// </summary>
public class NodeList : INodeList
{
    /// <summary>
    /// Список узлов.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    protected IList<INode> Nodes { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public INode? this[int index] => Nodes.ElementAtOrDefault(index);

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => Nodes.Count;

    internal NodeList(IEnumerable<INode> nodes) => Nodes = new List<INode>(nodes);

    internal NodeList() : this([]) { }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public void Add(INode node) => Nodes.Add(node);

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public IEnumerator<INode> GetEnumerator() => Nodes.GetEnumerator();

    [ScriptMember(ScriptAccess.None)]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}