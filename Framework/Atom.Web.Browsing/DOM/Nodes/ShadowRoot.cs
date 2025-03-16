using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет теневое дерево DOM.
/// </summary>
public class ShadowRoot : DocumentFragment, IShadowRoot
{
    /// <inheritdoc/>
    [ScriptMember("onslotchange")]
    public event Action<IEvent>? SlotChanged;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public ShadowRootMode Mode { get; }

    /// <inheritdoc/>
    [ScriptMember("delegatesFocus", ScriptAccess.ReadOnly)]
    public bool IsDelegatesFocused { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public SlotAssignmentMode SlotAssignment { get; }

    /// <inheritdoc/>
    [ScriptMember("clonable", ScriptAccess.ReadOnly)]
    public bool IsClonable { get; }

    /// <inheritdoc/>
    [ScriptMember("serializable", ScriptAccess.ReadOnly)]
    public bool IsSerializable { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement Host { get; }

    internal ShadowRoot(IElement host) : base(host.Uri, $"#{host.Name}") => Host = host;

    /// <summary>
    /// Происходит в момент изменения слота.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    protected virtual void OnSlotChange(IEvent e) => SlotChanged?.Invoke(e);
}