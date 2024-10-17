using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет обработчик события.
/// </summary>
public interface IEventListener
{
    /// <summary>
    /// Обрабатывает событие.
    /// </summary>
    /// <param name="event">Параметры события.</param>
    [ScriptMember]
    void HandleEvent(IEvent @event);
}