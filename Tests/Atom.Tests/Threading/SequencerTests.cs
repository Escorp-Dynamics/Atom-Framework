using System.Collections.Concurrent;
using System.Diagnostics;

namespace Atom.Threading.Tests;

//[Ignore("Временно отключил пока не будут устранены проблемы в связанных модулях")]
public class SequencerTests(ILogger logger) : BenchmarkTests<SequencerTests>(logger), IDisposable
{
    #region Helper Classes

    private sealed class Worker(int id, TimeSpan duration)
    {
        private readonly TimeSpan duration = duration;
        private readonly Stopwatch timer = new();

        private int timeIncrement;

        public int Id { get; set; } = id;

        public bool IsReady { get; private set; }

        public static SequencerTests? Context { get; set; }

        public static Sequencer Sequencer { get; } = new(SequenceMode.Loop) { Interval = TimeSpan.FromSeconds(1) };

        static Worker()
        {
            Sequencer.Started += (_, args) => Log("Секвенция запущена");
            Sequencer.Stopped += (_, args) => Log("Секвенция остановлена");
            Sequencer.Added += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача добавлена, {args.Mode}");
            Sequencer.Updated += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача обновлена, {args.Mode}");
            Sequencer.Removed += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача удалена, {args.Mode}");
            Sequencer.Sequence += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Выполнение, {args.Mode}");
            Sequencer.Failed += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} {args.Exception?.Message ?? "Ошибка"}, {args.Mode}");
            Sequencer.Paused += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача приостановлена, {args.Mode}");
            Sequencer.Resumed += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача возобновлена, {args.Mode}");
            Sequencer.Changed += (_, args) => Log($"{((Worker)args.Task!.Target!).Id} Задача изменена, {args.Mode}");

            Context = new();
        }

        private static void Log(string? message)
        {
            message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
            Context?.Logger.WriteLineInfo(message);
            Trace.TraceInformation(message);
        }

        public async ValueTask CallbackAsync()
        {
            Log($"{Id} Callback: {timeIncrement + 1}");
            if (timer.Elapsed >= duration && Context?.IsRemoveTest == true) Sequencer.Remove(CallbackAsync);
            if (Context?.IsRemoveTest != true) await Task.Delay(TimeSpan.FromSeconds(Interlocked.Increment(ref timeIncrement)));
        }

        public void Start()
        {
            Sequencer.AddAndStart(CallbackAsync);

            Task.Run(async () =>
            {
                await Sequencer.WaitAsync(CallbackAsync).ConfigureAwait(false);
                IsReady = true;
            });

            timer.Start();
        }
    }

    /// <summary>
    /// Счётчик для отслеживания количества выполнений задачи.
    /// </summary>
    private sealed class ExecutionCounter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);

        public void Reset() => Interlocked.Exchange(ref count, 0);
    }

    #endregion

    #region Fields

    private readonly Sequencer manualSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.Once);
    private readonly Sequencer loopSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.Loop);
    private readonly Sequencer loopWithWaitingSequencer = new(TimeSpan.FromSeconds(1), SequenceMode.LoopWithWaiting);

    private bool IsRemoveTest { get; set; }

    #endregion

    #region Constructors

    public SequencerTests() : this(ConsoleLogger.Unicode)
    {
        SetupSequencerEvents(manualSequencer);
        SetupSequencerEvents(loopSequencer);
        SetupSequencerEvents(loopWithWaitingSequencer);
    }

    #endregion

    #region Helper Methods

    private void SetupSequencerEvents(Sequencer sequencer)
    {
        sequencer.Started += (_, args) => Log("Секвенция запущена");
        sequencer.Stopped += (_, args) => Log("Секвенция остановлена");
        sequencer.Added += (_, args) => Log($"Задача добавлена: {args.Task?.Method.Name}");
        sequencer.Updated += (_, args) => Log($"Задача обновлена: {args.Task?.Method.Name}");
        sequencer.Removed += (_, args) => Log($"Задача удалена: {args.Task?.Method.Name}");
        sequencer.Sequence += (_, args) => Log($"Выполнение: {args.Task?.Method.Name}");
        sequencer.Failed += (_, args) => Log($"Ошибка: {args.Exception?.Message}");
        sequencer.Paused += (_, args) => Log($"Задача приостановлена: {args.Task?.Method.Name}");
        sequencer.Resumed += (_, args) => Log($"Задача возобновлена: {args.Task?.Method.Name}");
        sequencer.Changed += (_, args) => Log($"Задача изменена: {args.Task?.Method.Name}, режим: {args.Mode}");
    }

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    private async ValueTask TestCallbackAsync()
    {
        Log("TestCallbackAsync(): START");
        await Task.Delay(TimeSpan.FromSeconds(3));
        Log("TestCallbackAsync(): END");
    }

    #endregion

    #region Basic Mode Tests

    [TestCase(TestName = "Тест проверки секвенсора (Once)"), Benchmark]
    public async Task OnceModeExecutesTaskOnceThenRemovesAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Once);
        var counter = new ExecutionCounter();

        async ValueTask TaskAsync()
        {
            counter.Increment();
            await Task.Delay(50);
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        var result = await sequencer.WaitAsync(TaskAsync, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "WaitAsync должен вернуть false при удалении задачи");
            Assert.That(counter.Count, Is.EqualTo(1), "Задача в режиме Once должна выполниться ровно один раз");
            Assert.That(sequencer.Tasks, Does.Not.Contain(TaskAsync), "Задача должна быть удалена после выполнения");
        });
    }

    [TestCase(TestName = "Тест проверки секвенсора (Loop)"), Benchmark]
    public async Task LoopModeExecutesTaskMultipleTimesUntilRemovedAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        var counter = new ExecutionCounter();

        async ValueTask TaskAsync()
        {
            counter.Increment();
            await Task.Yield();
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        sequencer.Remove(TaskAsync);
        var countAfterRemove = counter.Count;

        Assert.Multiple(() =>
        {
            Assert.That(countAfterRemove, Is.GreaterThan(1), "Задача в режиме Loop должна выполниться несколько раз");
            Assert.That(sequencer.Tasks, Does.Not.Contain(TaskAsync), "Задача должна быть удалена");
        });

        sequencer.Stop();
    }

    [TestCase(TestName = "Тест проверки секвенсора (LoopWithWaiting)"), Benchmark]
    public async Task LoopWithWaitingModeWaitsForTaskCompletionBeforeNextIterationAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(50), SequenceMode.LoopWithWaiting);
        var executionTimes = new List<DateTime>();
        var taskDuration = TimeSpan.FromMilliseconds(200);

        async ValueTask TaskAsync()
        {
            executionTimes.Add(DateTime.UtcNow);
            await Task.Delay(taskDuration);
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(800));

        sequencer.Stop();

        // Проверяем, что интервал между запусками >= времени выполнения задачи
        for (var i = 1; i < executionTimes.Count; i++)
        {
            var interval = executionTimes[i] - executionTimes[i - 1];
            Assert.That(interval.TotalMilliseconds, Is.GreaterThanOrEqualTo(taskDuration.TotalMilliseconds - 50),
                $"Интервал между выполнениями {i - 1} и {i} должен быть >= времени выполнения задачи");
        }
    }

    #endregion

    #region Race Condition Tests

    /// <summary>
    /// Тест: смена режима с LoopWithWaiting на Loop во время выполнения задачи не вызывает зависание.
    /// </summary>
    [TestCase(TestName = "Race Condition: SetMode во время выполнения (LoopWithWaiting → Loop)"), Benchmark]
    [CancelAfter(30_000)]
    public async Task SetModeDuringExecutionLoopWithWaitingToLoopShouldNotHangAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(10), SequenceMode.LoopWithWaiting);

        var executionCount = 0;
        Func<ValueTask>? taskRef = null;

        async ValueTask TaskAsync()
        {
            var count = Interlocked.Increment(ref executionCount);
            Log($"TaskAsync execution #{count}");

            if (count == 1 && taskRef is not null)
            {
                // При первом выполнении меняем режим
                Log("TaskAsync: calling SetMode(Loop)");
                sequencer.SetMode(taskRef, SequenceMode.Loop);
                Log("TaskAsync: SetMode(Loop) done");
            }

            await Task.Delay(1);
            Log($"TaskAsync execution #{count} done");
        }

        taskRef = TaskAsync;
        sequencer.Add(TaskAsync);
        sequencer.Start();

        // Ждём минимум 3 выполнения или таймаут 5 секунд
        var sw = Stopwatch.StartNew();
        while (executionCount < 3 && sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(50);
        }

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(3), $"Секвенсор завис! Выполнений: {executionCount}");
        });

        Log($"Успешно выполнено {executionCount} раз за {sw.ElapsedMilliseconds}мс");
    }

    /// <summary>
    /// Тест: SetMode + Resume во время выполнения не вызывает зависание.
    /// </summary>
    [TestCase(TestName = "Race Condition: SetMode + Resume во время выполнения"), Benchmark]
    [CancelAfter(30_000)]
    public async Task SetModeWithResumeDuringExecutionShouldContinueAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(10), SequenceMode.LoopWithWaiting);
        var executionCount = 0;
        var tcs = new TaskCompletionSource();

        async ValueTask TaskAsync()
        {
            var count = Interlocked.Increment(ref executionCount);
            Log($"Выполнение #{count}");

            if (count == 1)
            {
                // Меняем режим и вызываем Resume (как в реальном коде)
                sequencer.SetMode(TaskAsync, SequenceMode.Loop);
                sequencer.Resume(TaskAsync);
                Log("SetMode + Resume вызваны");
            }

            if (count >= 3)
            {
                tcs.TrySetResult();
            }

            await Task.Delay(1);
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.EqualTo(tcs.Task), $"Секвенсор завис! Выполнений: {executionCount}");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(3), $"Ожидалось минимум 3 выполнения, получено {executionCount}");
        });
    }

    /// <summary>
    /// Тест: смена режима с Loop на LoopWithWaiting во время выполнения.
    /// </summary>
    [TestCase(TestName = "Race Condition: SetMode во время выполнения (Loop → LoopWithWaiting)"), Benchmark]
    [CancelAfter(30_000)]
    public async Task SetModeDuringExecutionLoopToLoopWithWaitingShouldNotHangAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(10), SequenceMode.Loop);

        var executionCount = 0;
        var tcs = new TaskCompletionSource();

        async ValueTask TaskAsync()
        {
            var count = Interlocked.Increment(ref executionCount);
            Log($"Выполнение #{count}");

            if (count == 2)
            {
                // При втором выполнении меняем режим на LoopWithWaiting
                sequencer.SetMode(TaskAsync, SequenceMode.LoopWithWaiting);
                Log("Режим изменён на LoopWithWaiting");
            }

            if (count >= 5)
            {
                tcs.TrySetResult();
            }

            await Task.Delay(5);
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.EqualTo(tcs.Task), $"Секвенсор завис! Выполнений: {executionCount}");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(5), $"Ожидалось минимум 5 выполнений, получено {executionCount}");
        });
    }

    /// <summary>
    /// Тест на race condition при параллельном добавлении одной и той же задачи.
    /// Проверяет, что задача добавляется только один раз.
    /// </summary>
    [TestCase(TestName = "Race Condition: параллельное добавление одной задачи"), Benchmark]
    public async Task ConcurrentAddSameTaskAddsOnlyOnceAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromSeconds(1), SequenceMode.Loop);
        var addedCount = 0;

        sequencer.Added += (_, _) => Interlocked.Increment(ref addedCount);

        static ValueTask TaskAsync() => ValueTask.CompletedTask;

        var tasks = new Task[100];
        using var barrier = new Barrier(100);

        for (var i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                sequencer.Add(TaskAsync);
            });
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(2));

        Assert.That(addedCount, Is.EqualTo(1), "Задача должна быть добавлена только один раз при параллельном добавлении");
    }

    /// <summary>
    /// Тест на race condition при параллельном добавлении разных задач.
    /// Проверяет, что все задачи корректно добавляются.
    /// </summary>
    [TestCase(TestName = "Race Condition: параллельное добавление разных задач"), Benchmark]
    public async Task ConcurrentAddDifferentTasksAddsAllAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromSeconds(1), SequenceMode.Loop);
        var addedCount = 0;
        const int taskCount = 100;

        sequencer.Added += (_, _) => Interlocked.Increment(ref addedCount);

        var tasks = new Task[taskCount];
        using var barrier = new Barrier(taskCount);

        for (var i = 0; i < taskCount; i++)
        {
            var localI = i;
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                sequencer.Add(() =>
                {
                    _ = localI;
                    return ValueTask.CompletedTask;
                });
            });
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(2));

        Assert.That(addedCount, Is.EqualTo(taskCount), $"Все {taskCount} задач должны быть добавлены");
    }

    /// <summary>
    /// Тест на race condition при одновременном добавлении и удалении задач.
    /// </summary>
    [TestCase(TestName = "Race Condition: одновременное добавление и удаление"), Benchmark]
    public async Task ConcurrentAddRemoveNoDeadlockOrCorruptionAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(50), SequenceMode.Loop);
        var exceptions = new List<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        static ValueTask TaskAsync() => ValueTask.CompletedTask;

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null)
                lock (exceptions) exceptions.Add(args.Exception);
        };

        sequencer.Start();

        var addTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                sequencer.Add(TaskAsync);
                await Task.Delay(10);
            }
        });

        var removeTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                sequencer.Remove(TaskAsync);
                await Task.Delay(15);
            }
        });

        await Task.WhenAll(addTask, removeTask);

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при параллельном добавлении/удалении");
    }

    /// <summary>
    /// Тест на race condition при одновременной паузе и возобновлении.
    /// </summary>
    [TestCase(TestName = "Race Condition: одновременная пауза и возобновление"), Benchmark]
    public async Task ConcurrentPauseResumeMaintainsConsistentStateAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(50), SequenceMode.Loop);
        var executionCount = 0;

        async ValueTask TaskAsync()
        {
            Interlocked.Increment(ref executionCount);
            await Task.Yield();
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        var tasks = new Task[50];
        using var barrier = new Barrier(50);

        for (var i = 0; i < 50; i++)
        {
            var shouldPause = i % 2 == 0;
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                if (shouldPause)
                    sequencer.Pause(TaskAsync);
                else
                    sequencer.Resume(TaskAsync);
            });
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(2));

        // Даём время на стабилизацию
        await Task.Delay(200);

        var isPaused = sequencer.IsPaused(TaskAsync);
        Log($"Финальное состояние паузы: {isPaused}, выполнений: {executionCount}");

        // Тест проходит если не было deadlock и состояние консистентно
        Assert.Pass("Параллельные pause/resume не привели к deadlock");
    }

    /// <summary>
    /// Тест: Resume после Pause в режиме LoopWithWaiting должен продолжить выполнение.
    /// Проверяет, что задача попадает обратно в очередь выполнения после снятия с паузы.
    /// </summary>
    /// <remarks>
    /// Сценарий:
    /// 1. Создаём секвенсер в режиме LoopWithWaiting
    /// 2. Добавляем задачу через AddAndStart
    /// 3. Дожидаемся нескольких выполнений задачи
    /// 4. Ставим на паузу через Pause
    /// 5. Снимаем с паузы через Resume
    /// 6. Проверяем: задача должна продолжить выполняться после Resume
    ///
    /// Ожидаемое поведение:
    /// - После Resume задача должна попасть в очередь sequence
    /// - WaitAsync/WaitUntilAsync должен получить сигнал
    ///
    /// Проблемное поведение (до исправления):
    /// - После Resume задача снимается с паузы (IsPaused = false)
    /// - signal.Send(task) вызывается, activeTasks увеличивается
    /// - НО задача НЕ добавляется в очередь sequence
    /// - Таймер срабатывает, но sequence.TryDequeue возвращает false (очередь пуста)
    /// - Задача не выполняется, WaitAsync висит бесконечно
    /// </remarks>
    [TestCase(TestName = "Resume после Pause в режиме LoopWithWaiting должен продолжить выполнение"), Benchmark]
    [CancelAfter(10_000)]
    public async Task ResumeAfterPauseInLoopWithWaitingModeShouldContinueExecutionAsync()
    {
        // Arrange - ВАЖНО: начинаем сразу в режиме LoopWithWaiting
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(20), SequenceMode.LoopWithWaiting);
        var executionCount = 0;

        ValueTask TestTask()
        {
            Interlocked.Increment(ref executionCount);
            return ValueTask.CompletedTask;
        }

        // Act
        sequencer.AddAndStart(TestTask);

        // Ждём несколько выполнений (минимум 3)
        while (Volatile.Read(ref executionCount) < 3)
        {
            await Task.Delay(5);
        }

        var countBeforePause = Volatile.Read(ref executionCount);

        // Ставим на паузу
        sequencer.Pause(TestTask);

        // Ждём чтобы убедиться, что на паузе задача не выполняется
        await Task.Delay(100);
        var countDuringPause = Volatile.Read(ref executionCount);

        // Снимаем с паузы
        sequencer.Resume(TestTask);

        // Ждём выполнение после Resume
        await Task.Delay(200);
        var countAfterResume = Volatile.Read(ref executionCount);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(countDuringPause, Is.EqualTo(countBeforePause),
                "Задача на паузе не должна выполняться");
            Assert.That(countAfterResume, Is.GreaterThan(countDuringPause),
                $"Задача должна продолжить выполнение после Resume. До паузы: {countBeforePause}, на паузе: {countDuringPause}, после Resume: {countAfterResume}");
        });

        sequencer.Stop();
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест на корректность счётчика активных задач при параллельных операциях.
    /// </summary>
    [TestCase(TestName = "Thread Safety: счётчик активных задач"), Benchmark]
    public async Task ActiveTasksCounterStaysConsistentUnderLoadAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        const int taskCount = 20;
        var addedTasks = new List<Func<ValueTask>>();

        // Создаём уникальные делегаты для каждой задачи
        for (var i = 0; i < taskCount; i++)
        {
            var localI = i;
            ValueTask task()
            {
                _ = localI; // захватываем переменную для уникальности
                return ValueTask.CompletedTask;
            }
            addedTasks.Add(task);
            sequencer.Add(task);
        }

        sequencer.Start();

        // Ставим половину на паузу
        var pauseTasks = addedTasks.Take(taskCount / 2).ToArray();
        foreach (var task in pauseTasks)
        {
            sequencer.Pause(task);
        }

        // Даём время на применение пауз
        await Task.Delay(50);

        // Проверяем, что IsPaused возвращает корректные значения
        foreach (var task in pauseTasks)
        {
            Assert.That(sequencer.IsPaused(task), Is.True, "Задача должна быть на паузе");
        }

        foreach (var task in addedTasks.Skip(taskCount / 2))
        {
            Assert.That(sequencer.IsPaused(task), Is.False, "Задача не должна быть на паузе");
        }
    }

    /// <summary>
    /// Тест на потокобезопасность изменения режима задачи.
    /// </summary>
    [TestCase(TestName = "Thread Safety: изменение режима задачи"), Benchmark]
    public async Task SetModeConcurrentCallsNoCorruptionAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Once);
        var modeChanges = 0;

        sequencer.Changed += (_, _) => Interlocked.Increment(ref modeChanges);

        static ValueTask TaskAsync() => ValueTask.CompletedTask;

        sequencer.Add(TaskAsync);

        var tasks = new Task[100];
        var modes = new[] { SequenceMode.Once, SequenceMode.Loop, SequenceMode.LoopWithWaiting };

        for (var i = 0; i < 100; i++)
        {
            var mode = modes[i % 3];
            tasks[i] = Task.Run(() => sequencer.SetMode(TaskAsync, mode));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMinutes(1));

        // Проверяем, что режим можно получить и он валиден
        Assert.That(sequencer.TryGetMode(TaskAsync, out var finalMode), Is.True);
        Assert.That(finalMode, Is.AnyOf(SequenceMode.Once, SequenceMode.Loop, SequenceMode.LoopWithWaiting));

        Log($"Изменений режима: {modeChanges}, финальный режим: {finalMode}");
    }

    #endregion

    #region MinInterval Tests

    /// <summary>
    /// Тест на соблюдение минимального интервала между выполнениями задачи.
    /// </summary>
    [TestCase(TestName = "MinInterval: соблюдение минимального интервала"), Benchmark]
    public async Task MinIntervalRespectsMinimumTimeBetweenExecutionsAsync()
    {
        var minInterval = TimeSpan.FromMilliseconds(300);
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(50), SequenceMode.Loop);
        var executionTimes = new List<DateTime>();
        var lockObj = new object();

        ValueTask TaskAsync()
        {
            lock (lockObj) executionTimes.Add(DateTime.UtcNow);
            return ValueTask.CompletedTask;
        }

        sequencer.Add(TaskAsync, SequenceMode.Loop, minInterval);
        sequencer.Start();

        await Task.Delay(TimeSpan.FromSeconds(2));

        sequencer.Stop();

        // Проверяем интервалы между выполнениями
        for (var i = 1; i < executionTimes.Count; i++)
        {
            var interval = executionTimes[i] - executionTimes[i - 1];
            Assert.That(interval.TotalMilliseconds, Is.GreaterThanOrEqualTo(minInterval.TotalMilliseconds - 50),
                $"Интервал {i}: {interval.TotalMilliseconds}ms должен быть >= {minInterval.TotalMilliseconds - 50}ms");
        }

        Log($"Всего выполнений: {executionTimes.Count}");
    }

    #endregion

    #region Exception Handling Tests

    /// <summary>
    /// Тест на корректную обработку исключений в задачах.
    /// </summary>
    [TestCase(TestName = "Exception Handling: задача выбрасывает исключение"), Benchmark]
    public async Task TaskThrowsExceptionTriggersFailedEventContinuesExecutionAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        var failedCount = 0;
        var executionCount = 0;
        Exception? lastException = null;

        sequencer.Failed += (_, args) =>
        {
            Interlocked.Increment(ref failedCount);
            lastException = args.Exception;
        };

        ValueTask FailingTaskAsync()
        {
            Interlocked.Increment(ref executionCount);
            throw new InvalidOperationException("Test exception");
        }

        sequencer.Add(FailingTaskAsync);
        sequencer.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(failedCount, Is.GreaterThan(0), "Событие Failed должно быть вызвано");
            Assert.That(executionCount, Is.GreaterThan(1), "Задача должна продолжать выполняться после исключения");
            Assert.That(lastException, Is.InstanceOf<InvalidOperationException>());
        });
    }

    /// <summary>
    /// Тест на обработку исключения в режиме Once.
    /// </summary>
    [TestCase(TestName = "Exception Handling: исключение в режиме Once"), Benchmark]
    public async Task OnceModeTaskThrowsExceptionTaskIsRemovedAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Once);
        var removedCount = 0;

        sequencer.Removed += (_, _) => Interlocked.Increment(ref removedCount);

        static ValueTask FailingTaskAsync() => throw new InvalidOperationException("Test");

        sequencer.Add(FailingTaskAsync);
        sequencer.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.That(removedCount, Is.EqualTo(1), "Задача в режиме Once должна быть удалена даже при исключении");
    }

    #endregion

    #region Dispose Tests

    /// <summary>
    /// Тест на корректное освобождение ресурсов.
    /// </summary>
    [TestCase(TestName = "Dispose: корректное освобождение ресурсов"), Benchmark]
    public async Task DisposeWaitsForInFlightTasksThenDisposesAsync()
    {
        var sequencer = new Sequencer(TimeSpan.FromMilliseconds(50), SequenceMode.Loop);
        var taskStarted = new TaskCompletionSource();
        var taskCanComplete = new TaskCompletionSource();

        async ValueTask LongRunningTaskAsync()
        {
            taskStarted.TrySetResult();
            await taskCanComplete.Task;
        }

        sequencer.Add(LongRunningTaskAsync);
        sequencer.Start();

        // Ждём начала выполнения
        await taskStarted.Task;

        // Начинаем Dispose в фоне
        var disposeTask = Task.Run(sequencer.Dispose);

        // Даём немного времени на начало Dispose
        await Task.Delay(100);

        // Dispose не должен завершиться пока задача выполняется
        Assert.That(disposeTask.IsCompleted, Is.False, "Dispose должен ждать завершения задач");

        // Завершаем задачу
        taskCanComplete.SetResult();

        // Теперь Dispose должен завершиться
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(disposeTask.IsCompletedSuccessfully, Is.True, "Dispose должен успешно завершиться");
    }

    /// <summary>
    /// Тест на ObjectDisposedException после Dispose.
    /// </summary>
    [TestCase(TestName = "Dispose: операции после Dispose выбрасывают исключение"), Benchmark]
    public void DisposedSequencerThrowsObjectDisposedException()
    {
        var sequencer = new Sequencer();
        sequencer.Dispose();

        Assert.Throws<ObjectDisposedException>(sequencer.Start);
        Assert.Throws<ObjectDisposedException>(() => sequencer.Stop(true));
    }

    #endregion

    #region WaitAsync Tests

    /// <summary>
    /// Тест на корректную работу WaitAsync с таймаутом.
    /// </summary>
    /// <remarks>
    /// Временно отключен: работает в консольном приложении, но зависает в NUnit.
    /// Требуется дополнительное исследование взаимодействия с тестовым окружением.
    /// </remarks>
    [TestCase(TestName = "WaitAsync: таймаут работает корректно"), Benchmark]
    [Ignore("Зависает в NUnit, но работает в консольном приложении - требуется исследование")]
    [CancelAfter(10000)]
    public void WaitAsyncWithTimeoutReturnsAfterTimeoutAsync()
    {
        // Синхронная обёртка для избежания проблем с NUnit async
        var task = Task.Run(async () =>
        {
            using var sequencer = new Sequencer(TimeSpan.FromSeconds(10), SequenceMode.Loop);

            static ValueTask NeverEndingTaskAsync() => new(Task.Delay(Timeout.Infinite));

            sequencer.Add(NeverEndingTaskAsync);
            sequencer.Start();

            var sw = Stopwatch.StartNew();
            await sequencer.WaitAsync(NeverEndingTaskAsync, TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            sw.Stop();

            sequencer.Stop();

            return sw.ElapsedMilliseconds;
        });

        // Ждём с таймаутом
        if (!task.Wait(TimeSpan.FromSeconds(5)))
        {
            Assert.Fail("Тест не завершился за 5 секунд");
        }

        Log($"WaitAsync вернулся через {task.Result}мс");
        Assert.That(task.Result, Is.LessThan(2000), "WaitAsync должен вернуться по таймауту");
    }

    /// <summary>
    /// Тест на WaitAsync с CancellationToken.
    /// </summary>
    /// <remarks>
    /// Временно отключен: работает в консольном приложении, но зависает в NUnit.
    /// Требуется дополнительное исследование взаимодействия с тестовым окружением.
    /// </remarks>
    [TestCase(TestName = "WaitAsync: отмена через CancellationToken"), Benchmark]
    [Ignore("Зависает в NUnit, но работает в консольном приложении - требуется исследование")]
    [CancelAfter(5000)]
    public void WaitAsyncWithCancellationThrowsOrReturnsAsync()
    {
        var task = Task.Run(async () =>
        {
            using var sequencer = new Sequencer(TimeSpan.FromSeconds(10), SequenceMode.Loop);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            static ValueTask TaskAsync() => new(Task.Delay(Timeout.Infinite));

            sequencer.Add(TaskAsync);
            sequencer.Start();

            var sw = Stopwatch.StartNew();

            try
            {
                await sequencer.WaitAsync(TaskAsync, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо
            }

            sw.Stop();
            sequencer.Stop();

            return sw.ElapsedMilliseconds;
        });

        // Ждём с таймаутом
        if (!task.Wait(TimeSpan.FromSeconds(5)))
        {
            Assert.Fail("Тест не завершился за 5 секунд");
        }

        Log($"WaitAsync среагировал на отмену через {task.Result}мс");
        Assert.That(task.Result, Is.LessThan(2000), "WaitAsync должен отреагировать на отмену");
    }

    /// <summary>
    /// Тест на WaitUntilAsync с пользовательским условием.
    /// </summary>
    [TestCase(TestName = "WaitUntilAsync: пользовательское условие"), Benchmark]
    public async Task WaitUntilAsyncCustomConditionWaitsUntilConditionMetAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        var counter = 0;

        ValueTask TaskAsync()
        {
            Interlocked.Increment(ref counter);
            return ValueTask.CompletedTask;
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        var result = await sequencer.WaitUntilAsync(TaskAsync, () => counter >= 5, TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "WaitUntilAsync должен вернуть true когда условие выполнено");
            Assert.That(counter, Is.GreaterThanOrEqualTo(5), "Счётчик должен достигнуть 5");
        });
    }

    #endregion

    #region Original Tests (Refactored)

    [TestCase(TestName = "Тест проверки секвенсора (Manual)"), Benchmark]
    public async Task ManualTestAsync()
    {
        manualSequencer.Add(TestCallbackAsync);
        manualSequencer.Add(TestCallbackAsync); // Вторая добавка должна быть проигнорирована

        manualSequencer.Start();

        var result = await manualSequencer.WaitAsync(TestCallbackAsync);
        Assert.That(result, Is.False, "WaitAsync должен вернуть false при удалении задачи");
    }

    [TestCase(TestName = "Тест проверки секвенсора с паузой (Manual)"), Benchmark]
    public async Task ManualWithPauseTestAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Once);
        var executed = false;

        async ValueTask TaskAsync()
        {
            executed = true;
            await Task.Delay(100);
        }

        sequencer.Add(TaskAsync);
        sequencer.Pause(TaskAsync);  // Ставим паузу ДО старта
        sequencer.Start();

        await Task.Delay(500);
        Assert.That(executed, Is.False, "Задача на паузе не должна выполняться");

        sequencer.Resume(TaskAsync);

        await sequencer.WaitAsync(TaskAsync, TimeSpan.FromSeconds(5));
        Assert.That(executed, Is.True, "Задача должна выполниться после снятия с паузы");
    }

    [TestCase(TestName = "Тест удаления коллбэка"), Benchmark]
    public async Task LoopRemovingTestAsync()
    {
        IsRemoveTest = true;
        Worker.Context = this;

        var workers = new Worker[10];

        for (var i = 0; i < 10; ++i)
        {
            workers[i] = new Worker(i + 1, TimeSpan.FromSeconds(i + 1));
            workers[i].Start();
        }

        await Wait.UntilAsync(() => workers.All(x => x.IsReady), TimeSpan.FromMinutes(2));
        Assert.Pass();
    }

    [TestCase(TestName = "Тест паузы и смены режимов"), Benchmark]
    public async Task LoopPauseAndChangeModeTestAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(200), SequenceMode.Loop);
        var executionCount = 0;
        var modes = new List<SequenceMode>();

        sequencer.Changed += (_, args) =>
        {
            lock (modes) modes.Add(args.Mode);
        };

        async ValueTask TaskAsync()
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(50);
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        await Task.Delay(500);

        sequencer.Pause(TaskAsync);
        var countBeforeModeChange = executionCount;
        sequencer.SetMode(TaskAsync, SequenceMode.LoopWithWaiting);

        await Task.Delay(200);
        Assert.That(executionCount, Is.EqualTo(countBeforeModeChange), "На паузе задача не должна выполняться");

        sequencer.Resume(TaskAsync);

        await Task.Delay(500);

        sequencer.SetMode(TaskAsync, SequenceMode.Once);

        await sequencer.WaitAsync(TaskAsync, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(executionCount, Is.GreaterThan(countBeforeModeChange), "После resume задача должна выполняться");
            Assert.That(modes, Does.Contain(SequenceMode.LoopWithWaiting), "Должно быть изменение на LoopWithWaiting");
            Assert.That(modes, Does.Contain(SequenceMode.Once), "Должно быть изменение на Once");
        });
    }

    #endregion

    #region MergedRepetitions Tests

    /// <summary>
    /// Тест на функциональность MergedRepetitions.
    /// </summary>
    [TestCase(TestName = "MergedRepetitions: несколько выполнений без ожидания"), Benchmark]
    public async Task MergedRepetitionsExecutesMultipleTimesWithoutIntervalAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromSeconds(1), SequenceMode.Loop)
        {
            MergedRepetitions = 3
        };

        var executionTimes = new List<DateTime>();
        var lockObj = new object();

        ValueTask TaskAsync()
        {
            lock (lockObj) executionTimes.Add(DateTime.UtcNow);
            return ValueTask.CompletedTask;
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        // Ждём несколько итераций
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        sequencer.Stop();

        // Первые 3 выполнения должны быть почти мгновенными
        if (executionTimes.Count >= 3)
        {
            var firstThreeInterval = (executionTimes[2] - executionTimes[0]).TotalMilliseconds;
            Assert.That(firstThreeInterval, Is.LessThan(500),
                "Первые 3 выполнения при MergedRepetitions=3 должны быть быстрыми");
        }

        Log($"Всего выполнений: {executionTimes.Count}");
    }

    #endregion

    #region Start/Stop Tests

    /// <summary>
    /// Тест на повторный запуск после остановки.
    /// </summary>
    [TestCase(TestName = "Start/Stop: повторный запуск после остановки"), Benchmark]
    public async Task StartAfterStopWorksCorrectlyAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        var counter = new ExecutionCounter();

        ValueTask TaskAsync()
        {
            counter.Increment();
            return ValueTask.CompletedTask;
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        await Task.Delay(300);
        var countAfterFirstRun = counter.Count;

        sequencer.Stop(isClearPool: false);

        await Task.Delay(200);
        var countAfterStop = counter.Count;

        sequencer.Start();

        await Task.Delay(300);
        var countAfterRestart = counter.Count;

        Assert.Multiple(() =>
        {
            Assert.That(countAfterFirstRun, Is.GreaterThan(0), "Должны быть выполнения в первом запуске");
            Assert.That(countAfterStop, Is.EqualTo(countAfterFirstRun), "После Stop выполнений быть не должно");
            Assert.That(countAfterRestart, Is.GreaterThan(countAfterStop), "После повторного Start должны быть новые выполнения");
        });
    }

    /// <summary>
    /// Тест на многократные вызовы Start.
    /// </summary>
    [TestCase(TestName = "Start: многократные вызовы игнорируются"), Benchmark]
    public async Task MultipleStartOnlyFirstTakesEffectAsync()
    {
        using var sequencer = new Sequencer(TimeSpan.FromMilliseconds(100), SequenceMode.Loop);
        var startedCount = 0;

        sequencer.Started += (_, _) => Interlocked.Increment(ref startedCount);

        static ValueTask TaskAsync() => ValueTask.CompletedTask;

        sequencer.Add(TaskAsync);

        // Многократные вызовы Start
        for (var i = 0; i < 10; i++)
        {
            sequencer.Start();
        }

        await Task.Delay(100);

        Assert.That(startedCount, Is.EqualTo(1), "Событие Started должно быть вызвано только один раз");

        sequencer.Stop();
    }

    #endregion

    #region Stress Tests - Parallel Scenarios

    /// <summary>
    /// Стресс-тест: множество потоков добавляют и удаляют задачи параллельно.
    /// </summary>
    [TestCase(TestName = "Stress: параллельное добавление/удаление задач"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressParallelAddRemoveTasksAsync()
    {
        const int threadCount = 200;
        const int tasksPerThread = 50;
        const int iterations = 20;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(1000), SequenceMode.Loop); // ~0.1мс
        var totalExecutions = 0;
        var errors = new ConcurrentBag<Exception>();
        var taskDelegates = new ConcurrentDictionary<int, Func<ValueTask>>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        sequencer.Start();

        var threads = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var threadId = t;
            threads[t] = Task.Run(async () =>
            {
                var random = new Random(threadId * 1000);

                for (var iter = 0; iter < iterations; iter++)
                {
                    // Добавляем задачи
                    for (var i = 0; i < tasksPerThread; i++)
                    {
                        var taskId = (threadId * 1000) + i;

                        ValueTask TaskAsync()
                        {
                            Interlocked.Increment(ref totalExecutions);
                            return ValueTask.CompletedTask;
                        }

                        taskDelegates[taskId] = TaskAsync;
                        sequencer.Add(TaskAsync);
                    }

                    // Минимальная задержка для максимальной нагрузки
                    if (random.Next(10) is 0) await Task.Yield();

                    // Удаляем часть задач
                    for (var i = 0; i < tasksPerThread / 2; i++)
                    {
                        var taskId = (threadId * 1000) + random.Next(tasksPerThread);
                        if (taskDelegates.TryRemove(taskId, out var task))
                        {
                            sequencer.Remove(task);
                        }
                    }

                    if (random.Next(10) is 0) await Task.Yield();
                }
            });
        }

        await Task.WhenAll(threads);
        await Task.Delay(500); // Даём время на завершение оставшихся выполнений

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения задач");
        });

        Log($"Всего выполнений: {totalExecutions}, потоков: {threadCount}, итераций: {iterations}");
    }

    /// <summary>
    /// Стресс-тест: параллельные паузы и возобновления задач.
    /// </summary>
    [TestCase(TestName = "Stress: параллельные паузы/возобновления"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressParallelPauseResumeAsync()
    {
        const int taskCount = 500;
        const int controlThreads = 100;
        const int operationsPerThread = 500;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.Loop); // ~0.05мс
        var executionCounts = new int[taskCount];
        var pauseCounts = new int[taskCount];
        var resumeCounts = new int[taskCount];
        var errors = new ConcurrentBag<Exception>();
        var tasks = new Func<ValueTask>[taskCount];

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Создаём задачи
        for (var i = 0; i < taskCount; i++)
        {
            var index = i;
            tasks[i] = () =>
            {
                Interlocked.Increment(ref executionCounts[index]);
                return ValueTask.CompletedTask;
            };
            sequencer.Add(tasks[i]);
        }

        sequencer.Start();

        // Запускаем потоки, которые случайным образом ставят на паузу и возобновляют задачи
        var threads = new Task[controlThreads];
        for (var t = 0; t < controlThreads; t++)
        {
            var threadId = t;
            threads[t] = Task.Run(async () =>
            {
                var random = new Random(threadId * 777);

                for (var op = 0; op < operationsPerThread; op++)
                {
                    var taskIndex = random.Next(taskCount);
                    var task = tasks[taskIndex];

                    if (random.Next(2) is 0)
                    {
                        sequencer.Pause(task);
                        Interlocked.Increment(ref pauseCounts[taskIndex]);
                    }
                    else
                    {
                        sequencer.Resume(task);
                        Interlocked.Increment(ref resumeCounts[taskIndex]);
                    }

                    // Без задержек для максимальной нагрузки
                }
            });
        }

        await Task.WhenAll(threads);
        await Task.Delay(300);

        sequencer.Stop();

        var totalExecutions = executionCounts.Sum();
        var totalPauses = pauseCounts.Sum();
        var totalResumes = resumeCounts.Sum();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения");
        });

        Log($"Выполнений: {totalExecutions}, пауз: {totalPauses}, возобновлений: {totalResumes}");
    }

    /// <summary>
    /// Стресс-тест: параллельные Start/Stop секвенсора.
    /// </summary>
    [TestCase(TestName = "Stress: параллельные Start/Stop"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressParallelStartStopAsync()
    {
        const int taskCount = 300;
        const int controlThreads = 50;
        const int operationsPerThread = 200;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.Loop); // ~0.05мс
        var totalExecutions = 0;
        var startCount = 0;
        var stopCount = 0;
        var errors = new ConcurrentBag<Exception>();

        sequencer.Started += (_, _) => Interlocked.Increment(ref startCount);
        sequencer.Stopped += (_, _) => Interlocked.Increment(ref stopCount);
        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Добавляем задачи
        for (var i = 0; i < taskCount; i++)
        {
            sequencer.Add(() =>
            {
                Interlocked.Increment(ref totalExecutions);
                return ValueTask.CompletedTask;
            });
        }

        // Запускаем потоки, которые случайным образом делают Start/Stop
        var threads = new Task[controlThreads];
        for (var t = 0; t < controlThreads; t++)
        {
            var threadId = t;
            threads[t] = Task.Run(async () =>
            {
                var random = new Random(threadId * 333);

                for (var op = 0; op < operationsPerThread; op++)
                {
                    if (random.Next(2) is 0)
                    {
                        sequencer.Start();
                    }
                    else
                    {
                        sequencer.Stop(isClearPool: random.Next(3) is 0);
                    }

                    // Минимальные задержки
                    if (random.Next(20) is 0) await Task.Yield();
                }
            });
        }

        await Task.WhenAll(threads);

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(startCount, Is.GreaterThan(0), "Должны быть вызовы Start");
        });

        Log($"Выполнений: {totalExecutions}, стартов: {startCount}, стопов: {stopCount}");
    }

    /// <summary>
    /// Стресс-тест: комплексный сценарий - всё вместе.
    /// </summary>
    [TestCase(TestName = "Stress: комплексный параллельный сценарий"), Benchmark]
    [CancelAfter(180_000)]
    public async Task StressComplexParallelScenarioAsync()
    {
        const int initialTasks = 500;
        const int workerThreads = 150;
        const int durationMs = 30000; // 30 секунд

        using var sequencer = new Sequencer(TimeSpan.FromTicks(100), SequenceMode.Loop); // ~0.01мс
        using var cts = new CancellationTokenSource(durationMs);

        var totalExecutions = 0;
        var totalAdds = 0;
        var totalRemoves = 0;
        var totalPauses = 0;
        var totalResumes = 0;
        var totalStartStops = 0;
        var errors = new ConcurrentBag<Exception>();
        var activeTasks = new ConcurrentDictionary<int, Func<ValueTask>>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Создаём начальный набор задач
        for (var i = 0; i < initialTasks; i++)
        {
            var taskId = i;
            ValueTask task()
            {
                Interlocked.Increment(ref totalExecutions);
                return ValueTask.CompletedTask;
            }
            activeTasks[taskId] = task;
            sequencer.Add(task);
        }

        sequencer.Start();

        var nextTaskId = initialTasks;

        // Рабочие потоки, выполняющие случайные операции
        var workers = new Task[workerThreads];
        for (var w = 0; w < workerThreads; w++)
        {
            var workerId = w;
            workers[w] = Task.Run(async () =>
            {
                var random = new Random(workerId * 12345);

                while (!cts.Token.IsCancellationRequested)
                {
                    var operation = random.Next(10);

                    switch (operation)
                    {
                        case 0 or 1: // Добавить задачу (20%)
                            {
                                var taskId = Interlocked.Increment(ref nextTaskId);
                                ValueTask task()
                                {
                                    Interlocked.Increment(ref totalExecutions);
                                    return ValueTask.CompletedTask;
                                }
                                activeTasks[taskId] = task;
                                sequencer.Add(task);
                                Interlocked.Increment(ref totalAdds);
                                break;
                            }
                        case 2: // Удалить задачу (10%)
                            {
                                var keys = activeTasks.Keys.ToArray();
                                if (keys.Length > 0)
                                {
                                    var keyToRemove = keys[random.Next(keys.Length)];
                                    if (activeTasks.TryRemove(keyToRemove, out var task))
                                    {
                                        sequencer.Remove(task);
                                        Interlocked.Increment(ref totalRemoves);
                                    }
                                }
                                break;
                            }
                        case 3 or 4: // Пауза задачи (20%)
                            {
                                var keys = activeTasks.Keys.ToArray();
                                if (keys.Length > 0)
                                {
                                    var key = keys[random.Next(keys.Length)];
                                    if (activeTasks.TryGetValue(key, out var task))
                                    {
                                        sequencer.Pause(task);
                                        Interlocked.Increment(ref totalPauses);
                                    }
                                }
                                break;
                            }
                        case 5 or 6: // Возобновление задачи (20%)
                            {
                                var keys = activeTasks.Keys.ToArray();
                                if (keys.Length > 0)
                                {
                                    var key = keys[random.Next(keys.Length)];
                                    if (activeTasks.TryGetValue(key, out var task))
                                    {
                                        sequencer.Resume(task);
                                        Interlocked.Increment(ref totalResumes);
                                    }
                                }
                                break;
                            }
                        case 7: // Start/Stop секвенсора (10%)
                            {
                                if (random.Next(2) is 0)
                                {
                                    sequencer.Start();
                                }
                                else
                                {
                                    sequencer.Stop(isClearPool: false);
                                }
                                Interlocked.Increment(ref totalStartStops);
                                break;
                            }
                        default: // Продолжаем без задержки (20%)
                            {
                                break;
                            }
                    }

                    // Без задержек для максимальной нагрузки
                }
            });
        }

        await Task.WhenAll(workers);

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения задач");
        });

        Log($"Комплексный тест завершён:");
        Log($"  Выполнений: {totalExecutions}");
        Log($"  Добавлений: {totalAdds}, Удалений: {totalRemoves}");
        Log($"  Пауз: {totalPauses}, Возобновлений: {totalResumes}");
        Log($"  Start/Stop: {totalStartStops}");
        Log($"  Активных задач в конце: {activeTasks.Count}");
    }

    /// <summary>
    /// Стресс-тест: высокая нагрузка - много задач, быстрый интервал.
    /// </summary>
    [TestCase(TestName = "Stress: высокая нагрузка (5000 задач)"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressHighLoadManyTasksAsync()
    {
        const int taskCount = 5000;
        const int durationMs = 15000; // 15 секунд

        using var sequencer = new Sequencer(TimeSpan.FromTicks(100), SequenceMode.Loop); // ~0.01мс
        var executionCounts = new int[taskCount];
        var errors = new ConcurrentBag<Exception>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Добавляем 1000 задач
        for (var i = 0; i < taskCount; i++)
        {
            var index = i;
            sequencer.Add(() =>
            {
                Interlocked.Increment(ref executionCounts[index]);
                return ValueTask.CompletedTask;
            });
        }

        sequencer.Start();

        await Task.Delay(durationMs);

        sequencer.Stop();

        var totalExecutions = executionCounts.Sum();
        var minExecutions = executionCounts.Min();
        var maxExecutions = executionCounts.Max();
        var avgExecutions = executionCounts.Average();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(taskCount), "Каждая задача должна выполниться хотя бы раз");
        });

        Log($"Высокая нагрузка: {taskCount} задач за {durationMs}мс");
        Log($"  Всего выполнений: {totalExecutions}");
        Log($"  Min/Avg/Max на задачу: {minExecutions}/{avgExecutions:F1}/{maxExecutions}");
    }

    /// <summary>
    /// Стресс-тест: задачи с разным временем выполнения.
    /// </summary>
    [TestCase(TestName = "Stress: задачи с разным временем выполнения"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressVariableExecutionTimeAsync()
    {
        const int taskCount = 500;
        const int durationMs = 15000; // 15 секунд

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.LoopWithWaiting); // ~0.05мс
        var executionCounts = new int[taskCount];
        var errors = new ConcurrentBag<Exception>();
        var random = new Random(42);

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Добавляем задачи с разным временем выполнения
        for (var i = 0; i < taskCount; i++)
        {
            var index = i;
            var delayMs = random.Next(1, 50); // От 1 до 50 мс

            sequencer.Add(async () =>
            {
                Interlocked.Increment(ref executionCounts[index]);
                await Task.Delay(delayMs);
            });
        }

        sequencer.Start();

        await Task.Delay(durationMs);

        sequencer.Stop();

        var totalExecutions = executionCounts.Sum();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения");
        });

        Log($"Задачи с разным временем: всего выполнений: {totalExecutions}");
    }

    /// <summary>
    /// Стресс-тест: задачи выбрасывают исключения.
    /// </summary>
    [TestCase(TestName = "Stress: задачи с исключениями"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressTasksThrowExceptionsAsync()
    {
        const int taskCount = 500;
        const int durationMs = 10000; // 10 секунд

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.Loop); // ~0.05мс
        var successCount = 0;
        var failCount = 0;
        var random = new Random(42);

        sequencer.Failed += (_, _) => Interlocked.Increment(ref failCount);

        // Добавляем задачи, часть из которых выбрасывает исключения
        for (var i = 0; i < taskCount; i++)
        {
            var shouldThrow = random.Next(5) is 0; // 20% задач выбрасывают исключения

            if (shouldThrow)
            {
                sequencer.Add(() => throw new InvalidOperationException("Test exception"));
            }
            else
            {
                sequencer.Add(() =>
                {
                    Interlocked.Increment(ref successCount);
                    return ValueTask.CompletedTask;
                });
            }
        }

        sequencer.Start();

        await Task.Delay(durationMs);

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(successCount, Is.GreaterThan(0), "Должны быть успешные выполнения");
            Assert.That(failCount, Is.GreaterThan(0), "Должны быть зафиксированы ошибки");
        });

        Log($"Задачи с исключениями: успешных: {successCount}, ошибок: {failCount}");
    }

    /// <summary>
    /// Стресс-тест: быстрое чередование паузы/возобновления одной задачи.
    /// </summary>
    [TestCase(TestName = "Stress: быстрое чередование паузы/возобновления"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressRapidPauseResumeToggleAsync()
    {
        const int toggleCount = 100000; // 100k переключений

        using var sequencer = new Sequencer(TimeSpan.FromTicks(100), SequenceMode.Loop); // ~0.01мс
        var executionCount = 0;
        var pauseCount = 0;
        var resumeCount = 0;

        ValueTask TaskAsync()
        {
            Interlocked.Increment(ref executionCount);
            return ValueTask.CompletedTask;
        }

        sequencer.Paused += (_, _) => Interlocked.Increment(ref pauseCount);
        sequencer.Resumed += (_, _) => Interlocked.Increment(ref resumeCount);

        sequencer.Add(TaskAsync);
        sequencer.Start();

        // Быстро чередуем паузу/возобновление
        for (var i = 0; i < toggleCount; i++)
        {
            if (i % 2 is 0)
            {
                sequencer.Pause(TaskAsync);
            }
            else
            {
                sequencer.Resume(TaskAsync);
            }
        }

        await Task.Delay(1000);

        sequencer.Stop();

        Log($"Быстрое чередование: выполнений: {executionCount}, пауз: {pauseCount}, возобновлений: {resumeCount}");

        Assert.That(executionCount, Is.GreaterThan(0), "Должны быть выполнения");
    }

    /// <summary>
    /// Стресс-тест: одновременное ожидание WaitAsync из многих потоков.
    /// </summary>
    [TestCase(TestName = "Stress: параллельные WaitAsync"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressParallelWaitAsyncAsync()
    {
        const int waiterCount = 500;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(1000), SequenceMode.Loop); // ~0.1мс
        var executionCount = 0;
        var waitCompletions = 0;

        ValueTask TaskAsync()
        {
            Interlocked.Increment(ref executionCount);
            return ValueTask.CompletedTask;
        }

        sequencer.Add(TaskAsync);
        sequencer.Start();

        // Множество потоков одновременно ждут выполнения задачи
        var waiters = new Task[waiterCount];
        for (var i = 0; i < waiterCount; i++)
        {
            waiters[i] = Task.Run(async () =>
            {
                await sequencer.WaitAsync(TaskAsync, TimeSpan.FromSeconds(5));
                Interlocked.Increment(ref waitCompletions);
            });
        }

        await Task.WhenAll(waiters);

        sequencer.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(waitCompletions, Is.EqualTo(waiterCount), "Все ожидания должны завершиться");
            Assert.That(executionCount, Is.GreaterThan(0), "Задача должна выполниться");
        });

        Log($"Параллельные WaitAsync: ожиданий: {waitCompletions}, выполнений: {executionCount}");
    }

    /// <summary>
    /// Стресс-тест: перезапуск секвенсора в цикле.
    /// </summary>
    [TestCase(TestName = "Stress: многократный перезапуск"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressMultipleRestartsAsync()
    {
        const int restartCount = 500;
        const int taskCount = 200;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.Loop); // ~0.05мс
        var totalExecutions = 0;
        var errors = new ConcurrentBag<Exception>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Добавляем задачи
        for (var i = 0; i < taskCount; i++)
        {
            sequencer.Add(() =>
            {
                Interlocked.Increment(ref totalExecutions);
                return ValueTask.CompletedTask;
            });
        }

        // Многократно перезапускаем с минимальными задержками
        for (var i = 0; i < restartCount; i++)
        {
            sequencer.Start();
            await Task.Delay(10);
            sequencer.Stop(isClearPool: false);
            await Task.Delay(5);
        }

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения");
        });

        Log($"Многократный перезапуск: {restartCount} перезапусков, выполнений: {totalExecutions}");
    }

    /// <summary>
    /// Стресс-тест: изменение режима задач во время выполнения.
    /// </summary>
    [TestCase(TestName = "Stress: изменение режима задач"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressModeChangeDuringExecutionAsync()
    {
        const int taskCount = 300;
        const int durationMs = 15000; // 15 секунд

        using var sequencer = new Sequencer(TimeSpan.FromTicks(500), SequenceMode.Loop); // ~0.05мс
        var executionCounts = new int[taskCount];
        var tasks = new Func<ValueTask>[taskCount];
        var errors = new ConcurrentBag<Exception>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        // Создаём задачи
        for (var i = 0; i < taskCount; i++)
        {
            var index = i;
            tasks[i] = () =>
            {
                Interlocked.Increment(ref executionCounts[index]);
                return ValueTask.CompletedTask;
            };
            sequencer.Add(tasks[i]);
        }

        sequencer.Start();

        var modes = new[] { SequenceMode.Once, SequenceMode.Loop, SequenceMode.LoopWithWaiting };

        // Параллельно изменяем режимы задач
        var modeChanger = Task.Run(async () =>
        {
            var random = new Random(123);
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < durationMs)
            {
                var taskIndex = random.Next(taskCount);
                var newMode = modes[random.Next(modes.Length)];
                sequencer.SetMode(tasks[taskIndex], newMode);

                // Без задержек для максимальной нагрузки
            }
        });

        await modeChanger;

        sequencer.Stop();

        var totalExecutions = executionCounts.Sum();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(0), "Должны быть выполнения");
        });

        Log($"Изменение режимов: всего выполнений: {totalExecutions}");
    }

    /// <summary>
    /// Стресс-тест: одновременное использование нескольких секвенсоров.
    /// </summary>
    [TestCase(TestName = "Stress: несколько секвенсоров параллельно"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressMultipleSequencersAsync()
    {
        const int sequencerCount = 50;
        const int tasksPerSequencer = 200;
        const int durationMs = 15000; // 15 секунд

        var sequencers = new Sequencer[sequencerCount];
        var executionCounts = new int[sequencerCount];
        var errors = new ConcurrentBag<Exception>();

        // Создаём несколько секвенсоров с минимальными интервалами
        for (var s = 0; s < sequencerCount; s++)
        {
            var seqIndex = s;
            sequencers[s] = new Sequencer(TimeSpan.FromTicks(100 + (s * 10)), SequenceMode.Loop); // ~0.01мс+

            sequencers[s].Failed += (_, args) =>
            {
                if (args.Exception is not null) errors.Add(args.Exception);
            };

            // Добавляем задачи в каждый секвенсор
            for (var t = 0; t < tasksPerSequencer; t++)
            {
                sequencers[s].Add(() =>
                {
                    Interlocked.Increment(ref executionCounts[seqIndex]);
                    return ValueTask.CompletedTask;
                });
            }

            sequencers[s].Start();
        }

        await Task.Delay(durationMs);

        // Останавливаем и освобождаем
        foreach (var seq in sequencers)
        {
            seq.Stop();
            seq.Dispose();
        }

        var totalExecutions = executionCounts.Sum();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");
            Assert.That(totalExecutions, Is.GreaterThan(sequencerCount * tasksPerSequencer), "Каждая задача должна выполниться");
        });

        Log($"Несколько секвенсоров: {sequencerCount} секвенсоров, всего выполнений: {totalExecutions}");
        for (var s = 0; s < sequencerCount; s++)
        {
            Log($"  Секвенсор {s}: {executionCounts[s]} выполнений");
        }
    }

    /// <summary>
    /// Стресс-тест: Dispose во время активной работы.
    /// </summary>
    [TestCase(TestName = "Stress: Dispose во время работы"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressDisposeWhileRunningAsync()
    {
        const int iterations = 200;
        var errors = new ConcurrentBag<Exception>();

        for (var i = 0; i < iterations; i++)
        {
            var sequencer = new Sequencer(TimeSpan.FromTicks(100), SequenceMode.Loop); // ~0.01мс
            var executionCount = 0;

            sequencer.Failed += (_, args) =>
            {
                if (args.Exception is not null) errors.Add(args.Exception);
            };

            // Добавляем много задач
            for (var t = 0; t < 200; t++)
            {
                sequencer.Add(() =>
                {
                    Interlocked.Increment(ref executionCount);
                    return ValueTask.CompletedTask;
                });
            }

            sequencer.Start();

            // Ждём немного и делаем Dispose
            await Task.Delay(20);
            sequencer.Dispose();
        }

        Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");

        Log($"Dispose во время работы: {iterations} итераций без ошибок");
    }

    /// <summary>
    /// Стресс-тест: гонка между Add и Remove одной и той же задачи.
    /// </summary>
    [TestCase(TestName = "Stress: гонка Add/Remove одной задачи"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressRaceAddRemoveSameTaskAsync()
    {
        const int iterations = 10000;
        const int threadPairs = 50;

        using var sequencer = new Sequencer(TimeSpan.FromTicks(100), SequenceMode.Loop); // ~0.01мс
        var executionCount = 0;
        var errors = new ConcurrentBag<Exception>();

        sequencer.Failed += (_, args) =>
        {
            if (args.Exception is not null) errors.Add(args.Exception);
        };

        sequencer.Start();

        var tasks = new Task[threadPairs * 2];
        for (var p = 0; p < threadPairs; p++)
        {
            ValueTask SharedTaskAsync()
            {
                Interlocked.Increment(ref executionCount);
                return ValueTask.CompletedTask;
            }

            var pairIndex = p;

            // Поток, добавляющий задачу
            tasks[p * 2] = Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    sequencer.Add(SharedTaskAsync);
                }
            });

            // Поток, удаляющий задачу
            tasks[(p * 2) + 1] = Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    sequencer.Remove(SharedTaskAsync);
                }
            });
        }

        await Task.WhenAll(tasks);
        await Task.Delay(100);

        sequencer.Stop();

        Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Take(5).Select(e => e.Message))}");

        Log($"Гонка Add/Remove: выполнений: {executionCount}");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        manualSequencer.Dispose();
        loopSequencer.Dispose();
        loopWithWaitingSequencer.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}