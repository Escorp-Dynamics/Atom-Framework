namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет субъект наблюдателя для событий. Может быть ограничен определённым количеством наблюдателей.
/// </summary>
/// <typeparam name="T">Тип аргументов события, содержащих информацию о наблюдаемом событии.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ObservableEvent{T}"/>.
/// </remarks>
/// <param name="maxObserverCount">Максимальное количество обработчиков, которые могут наблюдать за этим событием.</param>
public class ObservableEvent<T>(int maxObserverCount) where T : EventArgs
{
    private sealed class ObservableEventHandler<TEventArgs>(Func<TEventArgs, ValueTask> handler, ObservableEventHandlerOptions handlerOptions, string description)
    {
        public Func<TEventArgs, ValueTask> HandleObservedEvent { get; } = handler;
        public ObservableEventHandlerOptions Options { get; } = handlerOptions;
        public string Description { get; } = description;

        public override string ToString() => Description;
    }

    private readonly Dictionary<string, ObservableEventHandler<T>> observers = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ObservableEvent{T}"/>.
    /// </summary>
    public ObservableEvent() : this(0) { }

    /// <summary>
    /// Получает максимальное количество наблюдателей, которые могут наблюдать за этим событием.
    /// Значение ноль (0) указывает на неограниченное количество наблюдателей.
    /// </summary>
    public int MaxObserverCount { get; } = maxObserverCount;

    /// <summary>
    /// Получает текущее количество наблюдателей, которые наблюдают за этим событием.
    /// </summary>
    public int CurrentObserverCount => observers.Count;

    /// <summary>
    /// Добавляет функцию для наблюдения за событием, которая принимает аргумент типа T и возвращает void.
    /// Она будет обёрнута в ValueTask, чтобы её можно было ожидать.
    /// </summary>
    /// <param name="handler">Действие, обрабатывающее наблюдаемое событие.</param>
    /// <param name="handlerOptions">
    /// Параметры выполнения обработчика. По умолчанию ObservableEventHandlerOptions.None,
    /// что означает, что обработчик будет пытаться выполниться синхронно, ожидая результата выполнения.
    /// </param>
    /// <param name="description">Описание для наблюдателя.</param>
    /// <returns>Наблюдатель для этого наблюдаемого события.</returns>
    /// <exception cref="BiDiException">
    /// Выбрасывается, когда пользователь пытается добавить больше наблюдателей, чем позволяет это событие.
    /// </exception>
    public EventObserver<T> AddObserver(Action<T> handler, ObservableEventHandlerOptions handlerOptions = ObservableEventHandlerOptions.None, string description = "")
    {
        async ValueTask wrappedHandler(T args)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler(args);
            taskCompletionSource.TrySetResult(true);
            await taskCompletionSource.Task.ConfigureAwait(false);
        }

        return AddObserver(wrappedHandler, handlerOptions, description);
    }

    /// <summary>
    /// Добавляет функцию для наблюдения за событием, которая принимает аргумент типа T и возвращает Task.
    /// </summary>
    /// <param name="handler">Функция, возвращающая Task, которая обрабатывает наблюдаемое событие.</param>
    /// <param name="handlerOptions">
    /// <param name="description">Описание для наблюдателя.</param>
    /// Параметры выполнения обработчика. По умолчанию ObservableEventHandlerOptions.None,
    /// что означает, что обработчик будет пытаться выполниться синхронно, ожидая результата выполнения.
    /// </param>
    /// <returns>Наблюдатель для этого наблюдаемого события.</returns>
    /// <exception cref="BiDiException">
    /// Выбрасывается, когда пользователь пытается добавить больше наблюдателей, чем позволяет это событие.
    /// </exception>
    public EventObserver<T> AddObserver(Func<T, ValueTask> handler, ObservableEventHandlerOptions handlerOptions = ObservableEventHandlerOptions.None, string description = "")
    {
        if (MaxObserverCount > 0 && observers.Count == MaxObserverCount)
            throw new BiDiException($"""Это наблюдаемое событие позволяет только {MaxObserverCount} обработчик{(MaxObserverCount is 1 ? string.Empty : "ов")}""");

        var observerId = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(description)) description = $"EventObserver<{typeof(T).Name}> (id: {observerId})";

        observers.Add(observerId, new ObservableEventHandler<T>(handler, handlerOptions, description));
        return new EventObserver<T>(this, observerId);
    }

    /// <summary>
    /// Удаляет обработчик для этого наблюдаемого события.
    /// </summary>
    /// <param name="observerId">Идентификатор обработчика, обрабатывающего событие.</param>
    public void RemoveObserver(string observerId) => observers.Remove(observerId);

    /// <summary>
    /// Асинхронно уведомляет наблюдателей о возникновении этого наблюдаемого события.
    /// </summary>
    /// <param name="notifyData">Данные события.</param>
    /// <returns>Объект задачи, представляющий асинхронную операцию.</returns>
    public async ValueTask NotifyObserversAsync(T notifyData)
    {
        foreach (var observer in observers.Values)
        {
            if ((observer.Options & ObservableEventHandlerOptions.RunHandlerAsynchronously) is ObservableEventHandlerOptions.RunHandlerAsynchronously)
                _ = Task.Run(() => observer.HandleObservedEvent(notifyData)).ConfigureAwait(false);
            else
                await observer.HandleObservedEvent(notifyData).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Преобразует текущий экземпляр в строковое представление.
    /// </summary>
    public override string ToString() => $"ObservableEvent<{typeof(T).Name}> with observers:\n    {string.Join("\n    ", observers.Values)}";
}