using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет теневое дерево DOM.
/// </summary>
public interface IShadowRoot : IDocumentFragment
{
    /// <summary>
    /// Происходит в момент изменения слота.
    /// </summary>
    [ScriptMember("onslotchange")]
    event Action<IEvent>? SlotChanged;

    /// <summary>
    /// Режим теневого копирования.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    ShadowRootMode Mode { get; }

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember("delegatesFocus", ScriptAccess.ReadOnly)]
    bool IsDelegatesFocused { get; }

    /// <summary>
    /// Режим ассигнации слота.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    SlotAssignmentMode SlotAssignment { get; }

    /// <summary>
    /// Определяет, может ли теневое дерево клонироваться.
    /// </summary>
    [ScriptMember("clonable", ScriptAccess.ReadOnly)]
    bool IsClonable { get; }

    /// <summary>
    /// Определяет, может ли теневое дерево быть сериализованным.
    /// </summary>
    [ScriptMember("serializable", ScriptAccess.ReadOnly)]
    bool IsSerializable { get; }

    /// <summary>
    /// Хост, к которому подключено теневое дерево.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IElement Host { get; }
}