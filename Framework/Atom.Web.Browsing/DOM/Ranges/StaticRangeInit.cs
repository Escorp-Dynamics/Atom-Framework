using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет свойства инициализации статического диапазона.
/// </summary>
public class StaticRangeInit
{
    /// <summary>
    /// Начальный узел.
    /// </summary>
    [ScriptMember]
    public INode StartContainer { get; set; }

    /// <summary>
    /// Индекс начального узла.
    /// </summary>
    [ScriptMember]
    public int StartOffset { get; set; }

    /// <summary>
    /// Конечный узел.
    /// </summary>
    [ScriptMember]
    public INode EndContainer { get; set; }

    /// <summary>
    /// Индекс конечного узла.
    /// </summary>
    [ScriptMember]
    public int EndOffset { get; set; }

    internal StaticRangeInit(INode startContainer, int startOffset, INode endContainer, int endOffset)
    {
        StartContainer = startContainer;
        StartOffset = startOffset;
        EndContainer = endContainer;
        EndOffset = endOffset;
    }
}