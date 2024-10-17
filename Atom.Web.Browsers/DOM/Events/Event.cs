using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <inheritdoc/>
public class Event : IEvent
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string Type { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IEventTarget? Target { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IEventTarget? CurrentTarget { get; }

    /// <inheritdoc/>
    [ScriptMember("eventPhase", ScriptAccess.ReadOnly)]
    public ushort Phase { get; }

    /// <inheritdoc/>
    [ScriptMember("bubbles", ScriptAccess.ReadOnly)]
    public bool IsBubbles { get; }

    /// <inheritdoc/>
    [ScriptMember("cancelable", ScriptAccess.ReadOnly)]
    public bool IsCancelable { get; }

    /// <inheritdoc/>
    [ScriptMember("defaultPrevented", ScriptAccess.ReadOnly)]
    public bool IsDefaultPrevented { get; }

    /// <inheritdoc/>
    [ScriptMember("composed", ScriptAccess.ReadOnly)]
    public bool IsComposed { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public bool IsTrusted { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public DateTimeOffset TimeStamp { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public IEnumerable<IEventTarget> ComposedPath() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void StopPropagation() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void StopImmediatePropagation() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void PreventDefault() => throw new NotImplementedException();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Event"/>.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="eventInitDict">Свойства инициализации события.</param>
    public Event(string type, EventInit eventInitDict)
    {
        eventInitDict ??= EventInit.Default;

        Type = type;
        IsBubbles = eventInitDict.IsBubbles;
        IsCancelable = eventInitDict.IsCancelable;
        IsComposed = eventInitDict.IsComposed;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Event"/>.
    /// </summary>
    /// <param name="type">Тип события.</param>
    public Event(string type) : this(type, EventInit.Default) { }
}