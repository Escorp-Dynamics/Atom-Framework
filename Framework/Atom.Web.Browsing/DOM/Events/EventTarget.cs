using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <inheritdoc/>
public class EventTarget : IEventTarget
{
    private readonly Dictionary<string, List<EventListenerEntry>> eventListeners = [];

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, IEventListener callback, AddEventListenerOptions options)
    {
        if (callback is null) return;

        if (!eventListeners.TryGetValue(type, out var value))
        {
            value = [];
            eventListeners[type] = value;
        }

        value.Add(new EventListenerEntry(callback, options ?? new AddEventListenerOptions()));
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, IEventListener callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, Func<IEvent, bool> callback, AddEventListenerOptions options) => AddEventListener(type, new EventListener(e => callback(e)), options);

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, Func<IEvent, bool> callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, Action<IEvent> callback, AddEventListenerOptions options) => AddEventListener(type, new EventListener(callback), options);

    /// <inheritdoc/>
    [ScriptMember]
    public void AddEventListener(string type, Action<IEvent> callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public void RemoveEventListener(string type, IEventListener callback, EventListenerOptions options)
    {
        if (callback is null || !eventListeners.TryGetValue(type, out var value)) return;
        value.RemoveAll(entry => entry.Callback == callback && entry.Options.IsCaptured == (options?.IsCaptured ?? false));
    }

    /// <inheritdoc/>
    [ScriptMember]
    public bool DispatchEvent(IEvent @event)
    {
        if (@event == null) return false;

        if (eventListeners.TryGetValue(@event.Type, out var value))
        {
            foreach (var entry in value)
            {
                entry.Callback.HandleEvent(@event);
                if (entry.Options.IsOnce) RemoveEventListener(@event.Type, entry.Callback, entry.Options);
            }
        }

        return @event.IsDefaultPrevented;
    }

    private sealed class EventListenerEntry(IEventListener callback, AddEventListenerOptions options)
    {
        public IEventListener Callback { get; set; } = callback;
        public AddEventListenerOptions Options { get; set; } = options;
    }
}