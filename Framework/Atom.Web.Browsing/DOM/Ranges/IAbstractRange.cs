using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет базовую реализацию диапазона.
/// </summary>
public interface IAbstractRange
{
    /// <summary>
    /// Ссылка на стартовый узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode StartContainer { get; }

    /// <summary>
    /// Индекс стартового узла.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int StartOffset { get; }

    /// <summary>
    /// Ссылка на конечный узел.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    INode EndContainer { get; }

    /// <summary>
    /// Индекс конечного узла.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    int EndOffset { get; }

    /// <summary>
    /// Указывает, является ли диапазон замкнутым.
    /// </summary>
    [ScriptMember("collapsed", ScriptAccess.ReadOnly)]
    bool IsCollapsed { get; }
}