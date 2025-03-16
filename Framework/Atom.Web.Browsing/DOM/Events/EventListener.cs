using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <inheritdoc/>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="EventListener"/>.
/// </remarks>
/// <param name="handler">Обработчик события.</param>
public class EventListener(Action<IEvent> handler) : IEventListener
{
    private readonly Action<IEvent> handler = handler;

    /// <inheritdoc/>
    [ScriptMember]
    public void HandleEvent(IEvent @event) => handler?.Invoke(@event);
}