namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет наблюдателя для событий.
/// </summary>
/// <typeparam name="T">Тип аргументов события, содержащих информацию о наблюдаемом событии.</typeparam>
public class EventObserver<T> where T : EventArgs
{
    private readonly ObservableEvent<T> observableEvent;
    private readonly string observerId;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EventObserver{T}"/>.
    /// </summary>
    /// <param name="observableEvent">Наблюдаемое событие.</param>
    /// <param name="observerId">Идентификатор обработчика наблюдаемого события.</param>
    internal EventObserver(ObservableEvent<T> observableEvent, string observerId)
    {
        this.observableEvent = observableEvent;
        this.observerId = observerId;
    }

    /// <summary>
    /// Прекращает наблюдение за событием.
    /// </summary>
    public void UnObserve() => observableEvent.RemoveObserver(observerId);
}