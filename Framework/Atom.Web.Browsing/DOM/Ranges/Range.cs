using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет диапазон.
/// </summary>
public class Range : AbstractRange, IRange
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode CommonAncestorContainer { get; set; }

    internal Range(INode startContainer, int startOffset, INode endContainer, int endOffset, INode commonAncestorContainer)
        : base(startContainer, startOffset, endContainer, endOffset) => CommonAncestorContainer = commonAncestorContainer;

    /*public Range()
    {

    }*/

    /// <inheritdoc/>
    [ScriptMember]
    public IDocumentFragment CloneContents() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IRange CloneRange() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Collapse(bool toStart) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public short CompareBoundaryPoints(RangeMode how, IRange sourceRange) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public short ComparePoint(INode node, int offset) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void DeleteContents() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Detach() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public IDocumentFragment ExtractContents() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void InsertNode(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool IntersectsNode(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public bool IsPointInRange(INode node, int offset) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SelectNode(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SelectNodeContents(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetEnd(INode node, int offset) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetEndAfter(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetEndBefore(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetStart(INode node, int offset) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetStartAfter(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SetStartBefore(INode node) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void SurroundContents(INode newParent) => throw new NotImplementedException();
}