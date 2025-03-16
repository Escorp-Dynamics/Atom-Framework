using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет базовую реализацию диапазона.
/// </summary>
public abstract class AbstractRange : IAbstractRange
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode StartContainer { get; protected set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int StartOffset { get; protected set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public INode EndContainer { get; protected set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int EndOffset { get; protected set; }

    /// <inheritdoc/>
    [ScriptMember("collapsed", ScriptAccess.ReadOnly)]
    public bool IsCollapsed => StartContainer == EndContainer && StartOffset == EndOffset;

    internal AbstractRange(INode startContainer, int startOffset, INode endContainer, int endOffset)
    {
        StartContainer = startContainer;
        EndContainer = endContainer;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }
}