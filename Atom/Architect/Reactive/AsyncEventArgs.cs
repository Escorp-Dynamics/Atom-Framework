using System.Diagnostics;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет базовый набор аргументов асинхронного события.
/// </summary>
public class AsyncEventArgs : EventArgs
{
    private readonly Stopwatch timer = new();

    /// <summary>
    /// Номер текущей итерации.
    /// </summary>
    public uint Iteration { get; set; }

    /// <summary>
    /// Определяет, было ли событие отменено другим обработчиком.
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Длительность события.
    /// </summary>
    public TimeSpan Duration => timer.Elapsed;

    /// <summary>
    /// Токен отмены задачи.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>
    /// Прикреплённая фоновая задача, которую могут ожидать другие обработчики события.
    /// </summary>
    public Task Task { get; set; } = Task.CompletedTask;

    /// <summary>
    /// Указывает, требуется ли ожидание выполнения фоновой задачи, прикреплённой из другого обработчика события.
    /// </summary>
    public bool NeedAwaiting => !Task.IsCompleted;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AsyncEventArgs"/>.
    /// </summary>
    protected AsyncEventArgs()
    {
        Iteration = 1;
        timer.Start();
    }

    /// <summary>
    /// Сбрасывает таймер длительности события.
    /// </summary>
    /// <param name="isIncrementIteration">Следует ли увеличить счётчик итераций.</param>
    public void Reset(bool isIncrementIteration)
    {
        if (isIncrementIteration) ++Iteration;
        timer.Restart();
    }

    /// <summary>
    /// Сбрасывает таймер длительности события и увеличивает счётчик итераций.
    /// </summary>
    public void Reset() => Reset(true);

    /// <summary>
    /// Приостанавливает таймер длительности события.
    /// </summary>
    public void Pause() => timer.Stop();

    /// <summary>
    /// Возобновляет таймер длительности события.
    /// </summary>
    public void Resume() => timer.Start();
}