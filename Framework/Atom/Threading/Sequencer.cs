using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Buffers;

namespace Atom.Threading;

/// <summary>
/// Представляет секвенцию задач, выполняемых с заданным интервалом времени.
/// </summary>
public sealed class Sequencer : IDisposable, IAsyncDisposable
{
    private struct TaskState
    {
        public SequenceMode Mode;
        public ulong Counter;
        public bool IsPaused;
        public bool IsCustomCondition;
        public long MinInterval;
        public long LastExecutionTime;
    }

    private readonly ConcurrentDictionary<Func<ValueTask>, TaskState> tasks = ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Rent(() => new ConcurrentDictionary<Func<ValueTask>, TaskState>());
    private readonly ConcurrentQueue<Func<ValueTask>> sequence = ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Rent();
    private readonly Timer timer;
    private readonly Wait awaiter = new();

    private ulong mergedCounter;

    private bool isRunning;
    private bool isDisposed;

    /// <summary>
    /// Получает или задает интервал времени между выполнениями задач.
    /// </summary>
    /// <value>
    /// Интервал времени между выполнениями задач.
    /// </value>
    public TimeSpan Interval { get; set; }

    /// <summary>
    /// Количество повторений задачи без ожидания выполнения (только для режима <see cref="SequenceMode.Loop"/>).
    /// </summary>
    public ulong LoopRepetitionsWithoutWaiting { get; set; }

    /// <summary>
    /// Количество задач с игнорированием ожидания.
    /// </summary>
    public ulong MergedRepetitions { get; set; }

    /// <summary>
    /// Минимальный интервал повторений между одной задачей.
    /// </summary>
    public TimeSpan MinIntervalByTask { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Режим работы секвенсора.
    /// </summary>
    public SequenceMode Mode { get; set; }

    /// <summary>
    /// Получает значение, указывающее, выполняется ли в данный момент секвенция задач.
    /// </summary>
    /// <value>
    /// Значение true, если секвенция задач выполняется; в противном случае - false.
    /// </value>
    public bool IsRunning => isRunning;

    /// <summary>
    /// Коллекция задач.
    /// </summary>
    public IEnumerable<Func<ValueTask>> Tasks => tasks.Keys;

    /// <summary>
    /// Происходит в момент запуска секвенции.
    /// </summary>
    public event MutableEventHandler<MutableEventArgs>? Started;

    /// <summary>
    /// Происходит в момент остановки секвенции.
    /// </summary>
    public event MutableEventHandler<MutableEventArgs>? Stopped;

    /// <summary>
    /// Происходит в момент срабатывания секвенции.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Sequence;

    /// <summary>
    /// Происходит в момент добавления задачи.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Added;

    /// <summary>
    /// Происходит в момент обновления задачи.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Updated;

    /// <summary>
    /// Происходит в момент удаления задачи.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Removed;

    /// <summary>
    /// Происходит в момент приостановки задачи.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Paused;

    /// <summary>
    /// Происходит в момент возобновления задачи.
    /// </summary>
    public event MutableEventHandler<SequenceEventArgs>? Resumed;

    /// <summary>
    /// Происходит в момент исключения при выполнении задачи.
    /// </summary>
    public event MutableEventHandler<SequenceFailedEventArgs>? Failed;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/>.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer(TimeSpan interval, SequenceMode mode)
    {
        Interval = interval;
        Mode = mode;
        timer = new(OnTimerElapsed, default, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/>.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer(TimeSpan interval) : this(interval, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с заданным интервалом в миллисекундах и режимом.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач в миллисекундах.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer(int interval, SequenceMode mode) : this(TimeSpan.FromMilliseconds(interval), mode) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с заданным интервалом в миллисекундах.
    /// </summary>
    /// <param name="interval">Интервал между выполнениями задач в миллисекундах.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer(int interval) : this(interval, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с интервалом в одну секунду и заданным режимом.
    /// </summary>
    /// <param name="mode">Режим работы секвенсора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer(SequenceMode mode) : this(TimeSpan.FromSeconds(1), mode) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Sequencer"/> с интервалом в одну секунду и базовым режимом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Sequencer() : this(SequenceMode.Manual) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessTask(Func<ValueTask> task, TaskState state)
    {
        var minInterval = Volatile.Read(ref state.MinInterval);
        var lastExecutionTime = Volatile.Read(ref state.LastExecutionTime);

        if (!Volatile.Read(ref state.IsPaused) && (minInterval is 0 || DateTime.UtcNow.Ticks - lastExecutionTime >= minInterval))
        {
            if (Sequence.On(args => { args.Task = task; Mode = state.Mode; }))
            {
                _ = ExecuteAsync(task, state).ConfigureAwait(false);
                if (Volatile.Read(ref state.IsCustomCondition)) awaiter.Release();
            }
        }
        else
        {
            sequence.Enqueue(task);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateTimer()
    {
        if (tasks.IsEmpty)
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var needMerged = Interlocked.Increment(ref mergedCounter) < MergedRepetitions;
        timer.Change(!needMerged ? Interval : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        if (!needMerged) Interlocked.Exchange(ref mergedCounter, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnTimerElapsed(object? state)
    {
        if (Volatile.Read(ref isDisposed) || !Volatile.Read(ref isRunning)) return;
        if (sequence.TryDequeue(out var task) && tasks.TryGetValue(task, out var entry)) ProcessTask(task, entry);
        UpdateTimer();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task ExecuteAsync(Func<ValueTask> task, TaskState state)
    {
        if (state.Mode is SequenceMode.Loop)
        {
            if (Interlocked.Increment(ref state.Counter) >= LoopRepetitionsWithoutWaiting)
            {
                tasks.AddOrUpdate(task, state, static (_, s) => s with { Mode = SequenceMode.LoopWithWaiting, Counter = default, LastExecutionTime = DateTime.UtcNow.Ticks });
            }

            ScheduleTask(task, state.Mode);
        }

        try
        {
            await task().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Failed.On(args => { args.Exception = ex; args.Task = task; args.Mode = state.Mode; });
        }

        if (state.Mode is SequenceMode.LoopWithWaiting)
        {
            tasks.AddOrUpdate(task, state, static (_, s) => s with { LastExecutionTime = DateTime.UtcNow.Ticks });
            ScheduleTask(task, state.Mode);
        }
        else if (state.Mode is SequenceMode.Manual)
        {
            Remove(task);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ScheduleTask(Func<ValueTask> task, SequenceMode mode, bool update = true)
    {
        if (!tasks.TryGetValue(task, out _)) return;

        sequence.Enqueue(task);
        if (update) Updated.On(args => { args.Task = task; args.Mode = mode; });
    }

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Func<ValueTask> task, SequenceMode mode, TimeSpan minInterval)
    {
        var isEmpty = tasks.IsEmpty;
        tasks[task] = new TaskState { Mode = mode, MinInterval = minInterval.Ticks, LastExecutionTime = DateTime.UtcNow.Ticks - minInterval.Ticks };

        ScheduleTask(task, mode, default);

        if (Added.On(args => { args.Task = task; args.Mode = mode; }) && Volatile.Read(ref isRunning) && isEmpty) OnTimerElapsed(default);
    }

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Func<ValueTask> task, SequenceMode mode) => Add(task, mode, MinIntervalByTask);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Func<ValueTask> task, TimeSpan minInterval) => Add(task, Mode, minInterval);

    /// <summary>
    /// Добавляет массив задач в очередь задач для последовательного выполнения.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для добавления в очередь.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add([NotNull] params Func<ValueTask>[] tasks)
    {
        foreach (var task in tasks) Add(task, Mode);
    }

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, SequenceMode mode, TimeSpan minInterval, bool waitIntervalBeforeStarting)
    {
        Add(task, mode, minInterval);
        Start(waitIntervalBeforeStarting);
    }

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, SequenceMode mode, TimeSpan minInterval) => AddAndStart(task, mode, minInterval, default);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, SequenceMode mode, bool waitIntervalBeforeStarting) => AddAndStart(task, mode, MinIntervalByTask, waitIntervalBeforeStarting);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="mode">Режим работы секвенсора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, SequenceMode mode) => AddAndStart(task, mode, MinIntervalByTask);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, TimeSpan minInterval, bool waitIntervalBeforeStarting) => AddAndStart(task, Mode, minInterval, waitIntervalBeforeStarting);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="minInterval">Минимальный интервал между повторениями задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, TimeSpan minInterval) => AddAndStart(task, minInterval, default);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task, bool waitIntervalBeforeStarting) => AddAndStart(task, MinIntervalByTask, waitIntervalBeforeStarting);

    /// <summary>
    /// Добавляет задачу в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="task">Делегат, представляющий задачу для добавления в очередь.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(Func<ValueTask> task) => AddAndStart(task, Mode);

    /// <summary>
    /// Добавляет массив задач в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    /// <param name="tasks">Массив делегатов, представляющих задачи для добавления в очередь.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart(bool waitIntervalBeforeStarting, [NotNull] params Func<ValueTask>[] tasks)
    {
        Add(tasks);
        Start(waitIntervalBeforeStarting);
    }

    /// <summary>
    /// Добавляет массив задач в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для добавления в очередь.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart([NotNull] params Func<ValueTask>[] tasks) => AddAndStart(default, tasks);

    /// <summary>
    /// Удаляет массив задач из очереди задач для последовательного выполнения.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для удаления из очереди.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove([NotNull] params Func<ValueTask>[] tasks)
    {
        foreach (var task in tasks)
        {
            if (this.tasks.TryRemove(task, out var state))
            {
                awaiter.Release();

                if (Removed.On(args => { args.Task = task; args.Mode = state.Mode; }) && this.tasks.IsEmpty) timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Ставит массив задач на паузу.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для установки паузы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pause([NotNull] params Func<ValueTask>[] tasks)
    {
        foreach (var task in tasks)
        {
            if (this.tasks.TryGetValue(task, out var state) && !Volatile.Read(ref state.IsPaused))
            {
                this.tasks.AddOrUpdate(task, state, static (_, s) => s with { IsPaused = true });
                awaiter.Release();

                if (Paused.On(args => { args.Task = task; args.Mode = state.Mode; }) && !this.tasks.Values.Any(x => !Volatile.Read(ref x.IsPaused))) timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Снимает массив задач с паузы.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для снятия с паузы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Resume([NotNull] params Func<ValueTask>[] tasks)
    {
        var isPaused = !this.tasks.Values.Any(x => !Volatile.Read(ref x.IsPaused));

        foreach (var task in tasks)
        {
            if (!this.tasks.TryGetValue(task, out var state) || !Volatile.Read(ref state.IsPaused)) continue;

            this.tasks.AddOrUpdate(task, state, static (_, s) => s with { IsPaused = default });
            awaiter.Release();

            if (Resumed.On(args => { args.Task = task; args.Mode = state.Mode; }) && isPaused)
            {
                isPaused = default;
                timer.Change(Interval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// Запускает выполнение задач в секвенции.
    /// </summary>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start(bool waitIntervalBeforeStarting)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (Interlocked.CompareExchange(ref isRunning, true, Volatile.Read(ref isRunning))) return;
        if (!tasks.IsEmpty && !timer.Change(Interval, Timeout.InfiniteTimeSpan)) Interlocked.Exchange(ref isRunning, default);
        if (!Volatile.Read(ref isRunning)) return;

        Started.On();
        if (!waitIntervalBeforeStarting) _ = Task.Run(() => OnTimerElapsed(default)).ConfigureAwait(false);
    }

    /// <summary>
    /// Запускает выполнение задач в секвенции.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start() => Start(default);

    /// <summary>
    /// Останавливает выполнение задач в секвенции.
    /// </summary>
    /// <param name="isClearPool">Указывает, будет ли очищен пул задач.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Stop(bool isClearPool)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (!Interlocked.CompareExchange(ref isRunning, default, Volatile.Read(ref isRunning))) return;
        if (!timer.Change(Timeout.Infinite, Timeout.Infinite)) Interlocked.Exchange(ref isRunning, true);

        if (Volatile.Read(ref isRunning)) return;

        if (isClearPool)
        {
            sequence.Clear();
            tasks.Clear();
        }

        awaiter.Release();
        Stopped.On();
    }

    /// <summary>
    /// Останавливает выполнение задач в секвенции, очищая пул.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Stop() => Stop(true);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitAsync(Func<ValueTask> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await awaiter.LockAsync(() => Volatile.Read(ref isRunning) && tasks.ContainsKey(task) && !Volatile.Read(ref isDisposed), timeout, cancellationToken).ConfigureAwait(false);

        return !Volatile.Read(ref isRunning);
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, TimeSpan timeout) => WaitAsync(task, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, int timeout, CancellationToken cancellationToken) => WaitAsync(task, TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param> 
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, int timeout) => WaitAsync(task, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, CancellationToken cancellationToken) => WaitAsync(task, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task) => WaitAsync(task, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitAsync(Func<ValueTask> task, [NotNull] Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!tasks.TryGetValue(task, out var state)) return !Volatile.Read(ref isRunning);
        state.IsCustomCondition = true;
        var isCondition = false;

        await awaiter.LockAsync(() =>
        {
            isCondition = condition();
            if (isCondition) Remove(task);
            return Volatile.Read(ref isRunning) && tasks.ContainsKey(task) && !Volatile.Read(ref isDisposed) && !isCondition;
        }, timeout, cancellationToken).ConfigureAwait(false);

        return !Volatile.Read(ref isRunning) || isCondition;
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<bool> condition, TimeSpan timeout) => WaitAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<bool> condition, int timeout, CancellationToken cancellationToken) => WaitAsync(task, condition, TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<bool> condition, int timeout) => WaitAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<bool> condition, CancellationToken cancellationToken) => WaitAsync(task, condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<bool> condition) => WaitAsync(task, condition, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitAsync(Func<ValueTask> task, [NotNull] Func<ValueTask<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!tasks.TryGetValue(task, out var state)) return !isRunning;
        state.IsCustomCondition = true;
        var isCondition = false;

        await awaiter.LockAsync(async () =>
        {
            isCondition = await condition().ConfigureAwait(false);
            if (isCondition) Remove(task);
            return Volatile.Read(ref isRunning) && tasks.ContainsKey(task) && !Volatile.Read(ref isDisposed) && !isCondition;
        }, timeout, cancellationToken).ConfigureAwait(false);

        return !Volatile.Read(ref isRunning) || isCondition;
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, TimeSpan timeout) => WaitAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, int timeout, CancellationToken cancellationToken) => WaitAsync(task, condition, TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, int timeout) => WaitAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, CancellationToken cancellationToken) => WaitAsync(task, condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition) => WaitAsync(task, condition, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => awaiter.LockAsync(() => Volatile.Read(ref isRunning) && !Volatile.Read(ref isDisposed), timeout, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync(TimeSpan timeout) => WaitAsync(timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync(int timeout, CancellationToken cancellationToken) => WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync(int timeout) => WaitAsync(timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync(CancellationToken cancellationToken) => WaitAsync(Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WaitAsync() => WaitAsync(CancellationToken.None);

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        Stop();
        timer.Dispose();
        awaiter.Dispose();

        ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Return(tasks, x => x.Clear());
        ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Return(sequence, x => x.Clear());

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        Stop();
        await timer.DisposeAsync().ConfigureAwait(false);
        awaiter.Dispose();

        ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Return(tasks, x => x.Clear());
        ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Return(sequence, x => x.Clear());

        GC.SuppressFinalize(this);
    }
}