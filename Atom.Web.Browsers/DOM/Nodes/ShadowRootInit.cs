using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Параметры инициализации теневого дерева.
/// </summary>
public class ShadowRootInit
{
    /// <summary>
    /// Режим теневого копирования.
    /// </summary>
    [ScriptMember]
    public ShadowRootMode Mode { get; set; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("delegatesFocus")]
    public bool IsDelegatesFocused { get; set; }

    /// <summary>
    /// Режим ассигнации слота.
    /// </summary>
    [ScriptMember]
    public SlotAssignmentMode SlotAssignment { get; set; }

    /// <summary>
    /// Определяет, может ли теневое дерево клонироваться.
    /// </summary>
    [ScriptMember("clonable")]
    public bool IsClonable { get; set; }

    /// <summary>
    /// Определяет, может ли теневое дерево быть сериализованным.
    /// </summary>
    [ScriptMember("serializable")]
    public bool IsSerializable { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static ShadowRootInit Default { get; } = new();
}