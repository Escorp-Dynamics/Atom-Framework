#pragma warning disable CS0649, MA0051

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Buffers;

namespace Atom.Threading;

/// <summary>
/// Представляет секвенцию задач, выполняемых с заданным интервалом времени.
/// </summary>
public sealed class Sequencer : IDisposable, IAsyncDisposable
{
    private sealed class TaskState
    {
        /// <summary>
        /// ExecutionContext, захваченный при добавлении задачи.
        /// Используется для передачи контекста логирования (ILogger scopes) и других
        /// ambient-данных из потока, добавившего задачу, в поток таймера.
        /// </summary>
        public ExecutionContext? Context;
        public SequenceMode OriginalMode;
        public SequenceMode Mode;
        public long Counter;
        public bool IsPaused;
        public bool IsCustomCondition;
        public bool IsScheduled; // Флаг для оптимизации EnqueueOnce - O(1) вместо O(n)
        public long MinInterval;
        public long LastExecutionTime;
    }

    private readonly ConcurrentDictionary<Func<ValueTask>, TaskState> tasks = ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Rent(() => new ConcurrentDictionary<Func<ValueTask>, TaskState>());
    private readonly ConcurrentQueue<Func<ValueTask>> sequence = ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Rent();
    private readonly Timer timer;
    private readonly Signal<Func<ValueTask>> signal = new();

    private int activeTasks; // атомарно: сколько НЕ на паузе
    private int inFlight;

    private long mergedCounter;

    private bool isRunning;
    private bool isDisposed;

    private static readonly double TickToStopwatch = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

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
    public long LoopRepetitionsWithoutWaiting { get; set; }

    /// <summary>
    /// Количество задач с игнорированием ожидания.
    /// </summary>
    public long MergedRepetitions { get; set; }

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
    public event MutableEventHandler<object, MutableEventArgs>? Started;

    /// <summary>
    /// Происходит в момент остановки секвенции.
    /// </summary>
    public event MutableEventHandler<object, MutableEventArgs>? Stopped;

    /// <summary>
    /// Происходит в момент срабатывания секвенции.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Sequence;

    /// <summary>
    /// Происходит в момент добавления задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Added;

    /// <summary>
    /// Происходит в момент обновления задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Updated;

    /// <summary>
    /// Происходит в момент удаления задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Removed;

    /// <summary>
    /// Происходит в момент приостановки задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Paused;

    /// <summary>
    /// Происходит в момент возобновления задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Resumed;

    /// <summary>
    /// Происходит в момент изменения параметров задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceEventArgs>? Changed;

    /// <summary>
    /// Происходит в момент исключения при выполнении задачи.
    /// </summary>
    public event MutableEventHandler<object, SequenceFailedEventArgs>? Failed;

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
    public Sequencer() : this(SequenceMode.Once) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ProcessTask(Func<ValueTask> task, TaskState state)
    {
        var minInterval = Volatile.Read(ref state.MinInterval);
        var lastExecutionTime = Volatile.Read(ref state.LastExecutionTime);

        if (Volatile.Read(ref state.IsPaused)) return default;

        if (minInterval is not 0 && (NowTicks() - lastExecutionTime) < minInterval) return default;

        // Восстанавливаем ExecutionContext, захваченный при добавлении задачи.
        // Это необходимо для корректной работы логирования (ILogger scopes) и других
        // контекстно-зависимых механизмов — без этого данные из scope логера внешнего
        // потока не будут доступны в потоке таймера.
        if (state.Context is not null) ExecutionContext.Restore(state.Context);

        if (Sequence.On(this, args => { args.Task = task; args.Mode = state.Mode; }))
        {
            _ = ExecuteAsync(task).ConfigureAwait(false);
            if (Volatile.Read(ref state.IsCustomCondition)) signal.Send(task);
            return true;
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateTimer()
    {
        if (tasks.IsEmpty || !Volatile.Read(ref isRunning))
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

        // Снимок «сколько максимум попыток сделать за тик»
        var maxProbe = Math.Max(1, tasks.Count); // ConcurrentDictionary.Count O(1)


        for (var i = 0; i < maxProbe; i++)
        {
            if (!sequence.TryDequeue(out var task))
            {
                break;
            }
            if (!tasks.TryGetValue(task, out var taskState)) continue;


            // Сбрасываем флаг IsScheduled - задача извлечена из очереди
            Interlocked.Exchange(ref taskState.IsScheduled, value: false);

            // возвращаем true, только если реально запустили
            if (ProcessTask(task, taskState))
                break;

            // Задача не выполнилась (пауза или не прошёл minInterval) — возвращаем в очередь
            Interlocked.Exchange(ref taskState.IsScheduled, value: true);
            sequence.Enqueue(task);
        }

        UpdateTimer();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateLoopMode(Func<ValueTask> task, TaskState state)
    {
        var oldMode = state.Mode;
        var mode = state.OriginalMode is SequenceMode.Loop && LoopRepetitionsWithoutWaiting > 0 && Interlocked.Increment(ref state.Counter) >= LoopRepetitionsWithoutWaiting ? SequenceMode.LoopWithWaiting : SequenceMode.Loop;

        Interlocked.Exchange(ref state.Mode, mode);
        Interlocked.Exchange(ref state.Counter, default);
        Interlocked.Exchange(ref state.LastExecutionTime, NowTicks());

        if (oldMode != mode) Changed.On(this, args => { args.Task = task; args.Mode = mode; });

        ScheduleTask(task, mode);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task ExecuteAsync(Func<ValueTask> task)
    {
        if (!tasks.TryGetValue(task, out var state)) return;

        var modeAtStart = state.Mode;

        // Loop режим: сразу планируем следующее выполнение (не ждём завершения)
        if (modeAtStart is SequenceMode.Loop) UpdateLoopMode(task, state);

        Interlocked.Increment(ref inFlight);

        try
        {
            await task().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Failed.On(this, args => { args.Exception = ex; args.Task = task; args.Mode = state.Mode; });
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }

        if (!tasks.TryGetValue(task, out state))
        {
            return;
        }


        if (state.Mode is SequenceMode.Once)
        {
            Remove(task);
        }
        else if (modeAtStart is SequenceMode.Loop)
        {
            // Режим был Loop при старте — уже обработали через UpdateLoopMode в начале
            // Но если режим изменился на LoopWithWaiting во время выполнения, нужно перепланировать
            if (state.Mode is SequenceMode.LoopWithWaiting)
            {
                Interlocked.Exchange(ref state.LastExecutionTime, NowTicks());
                ScheduleTask(task, state.Mode);
            }
        }
        else
        {
            // Режим был LoopWithWaiting при старте — планируем после завершения
            // Если режим изменился на Loop, используем UpdateLoopMode
            if (state.Mode is SequenceMode.Loop)
            {
                UpdateLoopMode(task, state);
            }
            else
            {
                Interlocked.Exchange(ref state.LastExecutionTime, NowTicks());
                ScheduleTask(task, state.Mode);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ScheduleTask(Func<ValueTask> task, SequenceMode mode, bool update = true)
    {
        if (!tasks.TryGetValue(task, out var state))
        {
            return;
        }

        // Атомарно проверяем и устанавливаем флаг IsScheduled - O(1) вместо O(n) Contains
        if (Interlocked.CompareExchange(ref state.IsScheduled, value: true, comparand: false))
        {
            return; // Уже в очереди
        }

        sequence.Enqueue(task);
        if (update) Updated.On(this, args => { args.Task = task; args.Mode = mode; });
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

        // TryAdd атомарно проверяет наличие и добавляет
        // Если задача уже существует - просто выходим (это не ошибка)
        if (!tasks.TryAdd(task, new TaskState
        {
            Context = ExecutionContext.Capture(),
            OriginalMode = mode,
            Mode = mode,
            MinInterval = minInterval.Ticks,
            LastExecutionTime = NowTicks() - minInterval.Ticks,
        }))
        {
            return;  // Задача уже существует - это нормальная ситуация
        }

        Interlocked.Increment(ref activeTasks);  // новая задача активна
        ScheduleTask(task, mode, default);

        signal.Send(task);
        if (Added.On(this, args => { args.Task = task; args.Mode = mode; }) && Volatile.Read(ref isRunning) && isEmpty) OnTimerElapsed(default);
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
    public void Add([NotNull] params IEnumerable<Func<ValueTask>> tasks)
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
    public void AddAndStart(bool waitIntervalBeforeStarting, [NotNull] params IEnumerable<Func<ValueTask>> tasks)
    {
        Add(tasks);
        Start(waitIntervalBeforeStarting);
    }

    /// <summary>
    /// Добавляет массив задач в очередь задач для последовательного выполнения и запускает выполнение.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для добавления в очередь.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddAndStart([NotNull] params IEnumerable<Func<ValueTask>> tasks) => AddAndStart(default, tasks);

    /// <summary>
    /// Удаляет массив задач из очереди задач для последовательного выполнения.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для удаления из очереди.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove([NotNull] params IEnumerable<Func<ValueTask>> tasks)
    {
        foreach (var task in tasks)
        {
            // Атомарно пытаемся удалить задачу
            // Если задача уже удалена или не существует - это нормальная ситуация при параллельном доступе
            if (!this.tasks.TryRemove(task, out var state)) continue;

            if (!Volatile.Read(ref state.IsPaused)) Interlocked.Decrement(ref activeTasks);

            // Сбрасываем флаги для чистоты (на случай повторного добавления с тем же делегатом)
            state.IsCustomCondition = default;
            Interlocked.Exchange(ref state.IsScheduled, value: false);

            signal.Send(task);

            if (Removed.On(this, args => { args.Task = task; args.Mode = state.Mode; }) && this.tasks.IsEmpty)
            {
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                sequence.Clear();
            }
        }
    }

    /// <summary>
    /// Ставит массив задач на паузу.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для установки паузы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Pause([NotNull] params IEnumerable<Func<ValueTask>> tasks)
    {
        foreach (var task in tasks)
        {
            if (!this.tasks.TryGetValue(task, out var state)) continue;
            if (Interlocked.CompareExchange(ref state.IsPaused, value: true, comparand: false)) continue;

            signal.Send(task);
            Interlocked.Decrement(ref activeTasks);

            Paused.On(this, args => { args.Task = task; args.Mode = state.Mode; });
        }

        // Если активных не осталось — выключаем таймер
        if (Volatile.Read(ref activeTasks) is 0) timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Снимает массив задач с паузы.
    /// </summary>
    /// <param name="tasks">Массив делегатов, представляющих задачи для снятия с паузы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Resume([NotNull] params IEnumerable<Func<ValueTask>> tasks)
    {
        var anyResumed = false;

        foreach (var task in tasks)
        {
            if (!this.tasks.TryGetValue(task, out var state)) continue;
            if (!Interlocked.CompareExchange(ref state.IsPaused, value: false, comparand: true)) continue;

            anyResumed = true;

            // Добавляем задачу обратно в очередь выполнения
            // Это критично для режима LoopWithWaiting, где задача извлекается из очереди
            // после выполнения и добавляется обратно только после завершения
            ScheduleTask(task, state.Mode);

            signal.Send(task);
            Interlocked.Increment(ref activeTasks);

            Resumed.On(this, args => { args.Task = task; args.Mode = state.Mode; });
        }

        // Всегда перезапускаем таймер если хотя бы одна задача снята с паузы
        // Таймер мог быть остановлен в Pause когда activeTasks стал 0
        if (anyResumed) UpdateTimer();
    }

    /// <summary>
    /// Определяет, находится ли задача на паузе.
    /// </summary>
    /// <param name="task">Задача.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPaused(Func<ValueTask> task) => tasks.TryGetValue(task, out var state) && Volatile.Read(ref state.IsPaused);

    /// <summary>
    /// Устанавливает режим работы задаче.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="mode">Режим работы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMode(Func<ValueTask> task, SequenceMode mode)
    {
        if (!tasks.TryGetValue(task, out var state) || state.Mode == mode) return;

        Interlocked.Exchange(ref state.OriginalMode, mode);
        Interlocked.Exchange(ref state.Mode, mode);

        Changed.On(this, args => { args.Task = task; args.Mode = mode; });
    }

    /// <summary>
    /// Пытается вернуть режим работы задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    /// <param name="mode">Режим работы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMode(Func<ValueTask> task, out SequenceMode mode)
    {
        if (tasks.TryGetValue(task, out var state))
        {
            mode = state.Mode;
            return true;
        }

        mode = default;
        return default;
    }

    /// <summary>
    /// Запускает выполнение задач в секвенции.
    /// </summary>
    /// <param name="waitIntervalBeforeStarting">Указывает, требуется ли ожидать интервал перед запуском секвенции.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start(bool waitIntervalBeforeStarting)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (Interlocked.CompareExchange(ref isRunning, value: true, default)) return;
        if (!tasks.IsEmpty) UpdateTimer();

        Started.On(this);
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
        StopInternal(isClearPool);
    }

    /// <summary>
    /// Останавливает выполнение задач в секвенции, очищая пул.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Stop() => Stop(isClearPool: true);

    /// <summary>
    /// Внутренняя логика остановки без проверки isDisposed.
    /// Используется из Dispose() и публичного Stop().
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StopInternal(bool isClearPool)
    {
        // Атомарно пытаемся перевести isRunning из true в false
        if (!Interlocked.CompareExchange(ref isRunning, value: false, comparand: true)) return;

        // Останавливаем таймер
        timer.Change(Timeout.Infinite, Timeout.Infinite);

        if (isClearPool)
        {
            sequence.Clear();
            tasks.Clear();
        }

        signal.Send();
        Stopped.On(this);
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, false если задача была удалена.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitAsync(Func<ValueTask> task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using (var awaiter = new Wait<Func<ValueTask>>(signal, task))
        {
            await awaiter.LockUntilAsync(() =>
                !Volatile.Read(ref isRunning) ||
                Volatile.Read(ref isDisposed) ||
                !tasks.ContainsKey(task), timeout, cancellationToken).ConfigureAwait(false);
        }

        // Если задачи больше нет в словаре — она была удалена → возвращаем false
        // Если задача есть, но секвенсор остановлен — возвращаем true
        if (!tasks.ContainsKey(task)) return false;
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
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var awaiter = new Wait<Func<ValueTask>>(signal);
        await awaiter.LockUntilAsync(() => !Volatile.Read(ref isRunning) || Volatile.Read(ref isDisposed), timeout, cancellationToken).ConfigureAwait(false);
    }

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
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, [NotNull] Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!tasks.TryGetValue(task, out var state)) return !Volatile.Read(ref isRunning);
        state.IsCustomCondition = true;
        var isConditionMet = false;

        using (var awaiter = new Wait<Func<ValueTask>>(signal, task))
        {
            await awaiter.LockUntilAsync(() =>
            {
                isConditionMet = condition();
                if (isConditionMet) Remove(task);
                return !Volatile.Read(ref isRunning) || !tasks.ContainsKey(task) || Volatile.Read(ref isDisposed) || isConditionMet;
            }, timeout, cancellationToken).ConfigureAwait(false);
        }

        return !Volatile.Read(ref isRunning) || isConditionMet;
    }

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<bool> condition, TimeSpan timeout) => WaitUntilAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<bool> condition, int timeout, CancellationToken cancellationToken) => WaitUntilAsync(task, condition, TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<bool> condition, int timeout) => WaitUntilAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<bool> condition, CancellationToken cancellationToken) => WaitUntilAsync(task, condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<bool> condition) => WaitUntilAsync(task, condition, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, [NotNull] Func<ValueTask<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!tasks.TryGetValue(task, out var state)) return !isRunning;
        state.IsCustomCondition = true;
        var isCondition = false;

        using (var awaiter = new Wait<Func<ValueTask>>(signal, task))
        {
            await awaiter.LockUntilAsync(async () =>
            {
                isCondition = !await condition().ConfigureAwait(false);
                if (isCondition) Remove(task);
                return !Volatile.Read(ref isRunning) || !tasks.ContainsKey(task) || Volatile.Read(ref isDisposed) || isCondition;
            }, timeout, cancellationToken).ConfigureAwait(false);
        }

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
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, TimeSpan timeout) => WaitUntilAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, int timeout, CancellationToken cancellationToken) => WaitUntilAsync(task, condition, TimeSpan.FromMilliseconds(timeout), cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, int timeout) => WaitUntilAsync(task, condition, timeout, CancellationToken.None);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <param name="cancellationToken">Токен отмены для отслеживания запросов на отмену.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition, CancellationToken cancellationToken) => WaitUntilAsync(task, condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Асинхронно ожидает, пока таймер не будет выключен.
    /// </summary>
    /// <param name="task">Делегат, наличие которого ожидается.</param>
    /// <param name="condition">Дополнительное условие проверки делегата.</param>
    /// <returns>True, если секвенсор был остановлен на глобальном уровне, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> WaitUntilAsync(Func<ValueTask> task, Func<ValueTask<bool>> condition) => WaitUntilAsync(task, condition, CancellationToken.None);

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        StopInternal(isClearPool: false);

        // Ожидаем завершения in-flight задач с разумным таймаутом.
        // Используем 10 * Interval или минимум 1 секунду, чтобы дать задачам время завершиться,
        // но не блокировать бесконечно в случае thread starvation.
        var timeout = Math.Max((int)Interval.TotalMilliseconds * 10, 1000);
        Wait.Until(() => Volatile.Read(ref inFlight) is 0, timeout);

        timer.Dispose();

        ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Return(tasks, x => x.Clear());
        ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Return(sequence, x => x.Clear());
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="Sequencer"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        StopInternal(isClearPool: false);
        await Wait.UntilAsync(() => Volatile.Read(ref inFlight) is 0).ConfigureAwait(false);

        await timer.DisposeAsync().ConfigureAwait(false);

        ObjectPool<ConcurrentDictionary<Func<ValueTask>, TaskState>>.Shared.Return(tasks, x => x.Clear());
        ObjectPool<ConcurrentQueue<Func<ValueTask>>>.Shared.Return(sequence, x => x.Clear());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NowTicks() => (long)(Stopwatch.GetTimestamp() * TickToStopwatch);
}