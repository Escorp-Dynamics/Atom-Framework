using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет диапазон.
/// </summary>
public interface IRange : IAbstractRange
{
    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("START_TO_START", ScriptAccess.ReadOnly)]
    public const RangeMode StartToStart = RangeMode.StartToStart;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("START_TO_END", ScriptAccess.ReadOnly)]
    public const RangeMode StartToEnd = RangeMode.StartToEnd;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("END_TO_END", ScriptAccess.ReadOnly)]
    public const RangeMode EndToEnd = RangeMode.EndToEnd;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("END_TO_START", ScriptAccess.ReadOnly)]
    public const RangeMode EndToStart = RangeMode.EndToStart;

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode CommonAncestorContainer { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetStart(INode node, int offset);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetEnd(INode node, int offset);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetStartBefore(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetStartAfter(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetEndBefore(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SetEndAfter(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Collapse(bool toStart);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Collapse() => Collapse(default);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SelectNode(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SelectNodeContents(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    short CompareBoundaryPoints(RangeMode how, IRange sourceRange);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void DeleteContents();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    IDocumentFragment ExtractContents();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    IDocumentFragment CloneContents();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void InsertNode(INode node);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void SurroundContents(INode newParent);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    IRange CloneRange();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Detach();

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    bool IsPointInRange(INode node, int offset);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    short ComparePoint(INode node, int offset);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    bool IntersectsNode(INode node);
}