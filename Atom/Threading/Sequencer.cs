using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Threading;

/// <summary>
/// Представляет секвенцию задач, выполняемых с заданным интервалом времени.
/// </summary>
public sealed class Sequencer : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim locker = new(1, 1);
    private readonly ConcurrentQueue<Action> tasks = [];
    private readonly Timer timer;

    /// <summary>
    /// Получает или задает интервал времени между выполнениями задач.
    /// </summary>
    /// <value>
    /// Интервал времени между выполнениями задач.
    /// </value>
    public TimeSpan Interval { get; set; }

    /// <summary>
    /// Получает или задает значение, указывающее, является ли секвенция задач зацикленной.
    /// </summary>
    /// <value>
    /// Значение true, если секвенция задач зациклена; в противном случае - false.
    /// </value>
    public bool IsLoop { get; set; }

    /// <summary>
    /// Получает значение, указывающее, выполняется ли в данный момент секвенция задач.
    /// </summary>
    /// <value>
    /// Значение true, если секвенция задач выполняется; в противном случае - false.
    /// </value>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/>.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач.</param>
    /// <param name="isLoop">Указывает, является ли секвенция зацикленной.</param>
    public Sequencer(TimeSpan interval, bool isLoop)
    {
        Interval = interval;
        IsLoop = isLoop;
        timer = new(OnTimerElapsed, default, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/>.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач.</param>
    public Sequencer(TimeSpan interval) : this(interval, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач в миллисекундах.</param>
    /// <param name="isLoop">Указывает, является ли секвенция зацикленной.</param>
    public Sequencer(int interval, bool isLoop) : this(TimeSpan.FromMilliseconds(interval), isLoop) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач в миллисекундах.</param>
    public Sequencer(int interval) : this(interval, default) { }

    /// <summary>
    /// Вызывается при срабатывании таймера и выполняет следующую задачу в очереди.
    /// Если задача успешно выполнена и секвенция зациклена, задача возвращается в конец очереди.
    /// </summary>
    /// <param name="state">Объект, передающий состояние таймера.</param>
    private void OnTimerElapsed(object? state)
    {
        if (!tasks.TryDequeue(out var task) || task is null) return;
        if (IsLoop) tasks.Enqueue(task);

        task();
        timer.Change(Interval, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    public void Add(Action task) => tasks.Enqueue(task);

    /// <summary>
    /// Добавляет массив задач в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для добавления в очередь.</param>
    public void Add([NotNull] params Action[] tasks)
    {
        foreach (var task in tasks) Add(task);
    }

    /// <summary>
    /// Удаляет задачу из очереди задач для последовательного выполнения.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для удаления из очереди.</param>
    public void Remove(Action task)
    {
        var tempQueue = new ConcurrentQueue<Action>();
        while (tasks.TryDequeue(out var currentTask)) if (!currentTask.Equals(task)) tempQueue.Enqueue(currentTask);
        while (tempQueue.TryDequeue(out var currentTask)) tasks.Enqueue(currentTask);
    }

    /// <summary>
    /// Удаляет массив задач из очереди задач для последовательного выполнения.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для удаления из очереди.</param>
    public void Remove([NotNull] params Action[] tasks)
    {
        foreach (var task in tasks) Remove(task);
    }

    /// <summary>
    /// Асинхронно запускает выполнение задач в секвенции.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (IsRunning)
        {
            locker.Release();
            return;
        }

        if (timer.Change(Interval, Timeout.InfiniteTimeSpan)) IsRunning = true;
        locker.Release();
        OnTimerElapsed(default);
    }

    /// <summary>
    /// Асинхронно запускает выполнение задач в секвенции.
    /// </summary>
    public ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <summary>
    /// Асинхронно останавливает выполнение задач в секвенции.
    /// </summary>
    /// <param name="isClearPool">Указывает, будет ли очищен пул задач.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public async ValueTask StopAsync(bool isClearPool, CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!IsRunning)
        {
            locker.Release();
            return;
        }

        if (timer.Change(Timeout.Infinite, Timeout.Infinite))
        {
            IsRunning = false;
            if (isClearPool) tasks.Clear();
        }

        locker.Release();
    }

    /// <summary>
    /// Асинхронно останавливает выполнение задач в секвенции.
    /// </summary>
    /// <param name="isClearPool">Указывает, будет ли очищен пул задач.</param>
    public ValueTask StopAsync(bool isClearPool) => StopAsync(isClearPool, CancellationToken.None);

    /// <summary>
    /// Асинхронно останавливает выполнение задач в секвенции, очищая пул.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public ValueTask StopAsync(CancellationToken cancellationToken) => StopAsync(true, cancellationToken);

    /// <summary>
    /// Асинхронно останавливает выполнение задач в секвенции, очищая пул.
    /// </summary>
    public ValueTask StopAsync() => StopAsync(CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="interval">Интервал ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public async ValueTask<bool> WaitAsync(Action task, TimeSpan interval, CancellationToken cancellationToken)
    {
        await Wait.UntilAsync(() => IsRunning && tasks.Contains(task), interval, cancellationToken).ConfigureAwait(false);
        return !IsRunning;
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="interval">Интервал ожидания.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public ValueTask<bool> WaitAsync(Action task, TimeSpan interval) => WaitAsync(task, interval, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="interval">Интервал ожидания в миллисекундах.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public ValueTask<bool> WaitAsync(Action task, int interval, CancellationToken cancellationToken) => WaitAsync(task, TimeSpan.FromMilliseconds(interval), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param> 
    /// <param name="interval">Интервал ожидания в миллисекундах.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public ValueTask<bool> WaitAsync(Action task, int interval) => WaitAsync(task, interval, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с интервалом в 100 мс.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public ValueTask<bool> WaitAsync(Action task, CancellationToken cancellationToken) => WaitAsync(task, 100, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с интервалом в 500 мс.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    public ValueTask<bool> WaitAsync(Action task) => WaitAsync(task, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом.
    /// </summary>
    /// <param name="interval">Интервал ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public ValueTask WaitAsync(TimeSpan interval, CancellationToken cancellationToken) => Wait.UntilAsync(() => IsRunning, interval, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом.
    /// </summary>
    /// <param name="interval">Интервал ожидания.</param>
    public ValueTask WaitAsync(TimeSpan interval) => WaitAsync(interval, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="interval">Интервал ожидания в миллисекундах.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public ValueTask WaitAsync(int interval, CancellationToken cancellationToken) => WaitAsync(TimeSpan.FromMilliseconds(interval), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="interval">Интервал ожидания в миллисекундах.</param>
    public ValueTask WaitAsync(int interval) => WaitAsync(interval, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с интервалом в 500 мс.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    public ValueTask WaitAsync(CancellationToken cancellationToken) => WaitAsync(500, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен, с интервалом в 500 мс.
    /// </summary>
    public ValueTask WaitAsync() => WaitAsync(CancellationToken.None);

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public void Dispose()
    {
        timer.Dispose();
        locker.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync().ConfigureAwait(false);
        locker.Dispose();
        GC.SuppressFinalize(this);
    }
}