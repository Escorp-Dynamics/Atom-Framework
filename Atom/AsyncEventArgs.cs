using System.Diagnostics;

namespace Atom;

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
    /// Сбрасывает таймер длительности события.
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