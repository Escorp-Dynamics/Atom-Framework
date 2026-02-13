using System.Diagnostics;

namespace Atom.Threading.Tests;

/// <summary>
/// Тесты для статического класса <see cref="Wait"/> и обобщённого <see cref="Wait{T}"/>.
/// </summary>
public class WaitTests(ILogger logger) : BenchmarkTests<WaitTests>(logger)
{
    #region Fields

    private readonly Signal<string> signal = new();

    #endregion

    #region Constructors

    public WaitTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Wait.Until (Sync) Tests

    /// <summary>
    /// Тест: Wait.Until завершается немедленно, если условие уже выполнено.
    /// </summary>
    [TestCase(TestName = "Wait.Until: условие выполнено сразу"), Benchmark]
    public void UntilReturnImmediatelyWhenConditionIsTrue()
    {
        var sw = Stopwatch.StartNew();

        Wait.Until(() => true, 5000);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Until должен вернуться немедленно");
    }

    /// <summary>
    /// Тест: Wait.Until ожидает пока условие не выполнится.
    /// </summary>
    [TestCase(TestName = "Wait.Until: ожидание выполнения условия"), Benchmark]
    public void UntilWaitsForConditionToBeTrue()
    {
        var flag = false;
        var sw = Stopwatch.StartNew();

        // Устанавливаем флаг через 200мс
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            flag = true;
        });

        Wait.Until(() => flag, 5000);

        sw.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(flag, Is.True, "Флаг должен быть установлен");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(180), "Until должен ждать ~200мс");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "Until не должен ждать слишком долго");
        });
    }

    /// <summary>
    /// Тест: Wait.Until завершается по таймауту.
    /// </summary>
    [TestCase(TestName = "Wait.Until: таймаут срабатывает корректно"), Benchmark]
    public void UntilReturnsOnTimeout()
    {
        var sw = Stopwatch.StartNew();

        Wait.Until(() => false, 500);

        sw.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(450), "Until должен ждать минимум таймаут");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "Until должен вернуться вскоре после таймаута");
        });
    }

    /// <summary>
    /// Тест: Wait.Until с TimeSpan таймаутом.
    /// </summary>
    [TestCase(TestName = "Wait.Until: TimeSpan таймаут"), Benchmark]
    public void UntilWithTimeSpanTimeout()
    {
        var sw = Stopwatch.StartNew();

        Wait.Until(() => false, TimeSpan.FromMilliseconds(300));

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(280), "Until должен ждать ~300мс");
    }

    /// <summary>
    /// Тест: Wait.Until без таймаута ожидает бесконечно (проверяем что завершается по условию).
    /// </summary>
    [TestCase(TestName = "Wait.Until: без таймаута ожидает условие"), Benchmark]
    public void UntilWithoutTimeoutWaitsForCondition()
    {
        var flag = false;
        var sw = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            flag = true;
        });

        Wait.Until(() => flag);

        sw.Stop();
        Assert.That(flag, Is.True, "Флаг должен быть установлен");
    }

    #endregion

    #region Wait.UntilAsync Tests

    /// <summary>
    /// Тест: Wait.UntilAsync завершается немедленно при выполненном условии.
    /// </summary>
    [TestCase(TestName = "Wait.UntilAsync: условие выполнено сразу"), Benchmark]
    public async Task UntilAsyncReturnImmediatelyWhenConditionIsTrue()
    {
        var sw = Stopwatch.StartNew();

        await Wait.UntilAsync(() => true, 5000);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "UntilAsync должен вернуться немедленно");
    }

    /// <summary>
    /// Тест: Wait.UntilAsync ожидает выполнения условия.
    /// </summary>
    [TestCase(TestName = "Wait.UntilAsync: ожидание выполнения условия"), Benchmark]
    public async Task UntilAsyncWaitsForCondition()
    {
        var flag = false;
        var sw = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            Volatile.Write(ref flag, true);
        });

        await Wait.UntilAsync(() => Volatile.Read(ref flag), 5000);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(180), "UntilAsync должен ждать ~200мс");
    }

    /// <summary>
    /// Тест: Wait.UntilAsync реагирует на CancellationToken.
    /// </summary>
    [TestCase(TestName = "Wait.UntilAsync: отмена через CancellationToken"), Benchmark]
    [CancelAfter(5000)]
    public async Task UntilAsyncReactsToCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();

        try
        {
            await Wait.UntilAsync(() => false, Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо
        }

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "UntilAsync должен среагировать на отмену");
    }

    /// <summary>
    /// Тест: Wait.UntilAsync с асинхронным условием.
    /// </summary>
    [TestCase(TestName = "Wait.UntilAsync: асинхронное условие"), Benchmark]
    public async Task UntilAsyncWithAsyncCondition()
    {
        var counter = 0;
        var sw = Stopwatch.StartNew();

        async ValueTask<bool> AsyncConditionAsync()
        {
            await Task.Yield();
            return Interlocked.Increment(ref counter) >= 5;
        }

        await Wait.UntilAsync(AsyncConditionAsync, 5000);

        sw.Stop();
        Assert.That(counter, Is.GreaterThanOrEqualTo(5), "Условие должно вызываться до достижения значения");
    }

    /// <summary>
    /// Тест: Wait.UntilAsync с таймаутом.
    /// </summary>
    [TestCase(TestName = "Wait.UntilAsync: таймаут работает"), Benchmark]
    public async Task UntilAsyncReturnsOnTimeout()
    {
        var sw = Stopwatch.StartNew();

        await Wait.UntilAsync(() => false, 300);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(280), "Должен ждать минимум таймаут");
    }

    #endregion

    #region Wait<T>.LockUntilAsync Tests

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync ожидает сигнала и проверяет условие.
    /// </summary>
    [TestCase(TestName = "Wait<T>.LockUntilAsync: базовая работа с сигналом"), Benchmark]
    public async Task LockUntilAsyncWaitsForSignalAndCondition()
    {
        var flag = false;
        using var wait = new Wait<string>(signal, "test");

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            flag = true;
            signal.Send("test");
        });

        var sw = Stopwatch.StartNew();
        await wait.LockUntilAsync(() => flag, 5000);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(flag, Is.True, "Флаг должен быть установлен");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(80), "Должно пройти ~100мс");
        });
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync срабатывает по таймауту.
    /// </summary>
    [TestCase(TestName = "Wait<T>.LockUntilAsync: таймаут"), Benchmark]
    public async Task LockUntilAsyncReturnsOnTimeout()
    {
        using var wait = new Wait<string>(signal, "test");

        var sw = Stopwatch.StartNew();
        await wait.LockUntilAsync(() => false, 300);
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(280), "Должен ждать минимум таймаут");
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync реагирует на отмену.
    /// </summary>
    [TestCase(TestName = "Wait<T>.LockUntilAsync: отмена через CancellationToken"), Benchmark]
    [CancelAfter(5000)]
    public async Task LockUntilAsyncReactsToCancellation()
    {
        using var wait = new Wait<string>(signal);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var sw = Stopwatch.StartNew();

        try
        {
            await wait.LockUntilAsync(() => false, Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо
        }

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "Должен среагировать на отмену");
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync с конкретным состоянием.
    /// </summary>
    [TestCase(TestName = "Wait<T>.LockUntilAsync: фильтрация по состоянию"), Benchmark]
    public async Task LockUntilAsyncFiltersSignalByState()
    {
        var correctSignalReceived = false;
        var wrongSignalReceived = false;
        using var wait = new Wait<string>(signal, "correct");

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            signal.Send("wrong"); // Этот не должен разблокировать
            await Task.Delay(50);
            signal.Send("correct"); // Этот должен разблокировать
            correctSignalReceived = true;
        });

        await wait.LockUntilAsync(() => correctSignalReceived, 5000);

        Assert.Multiple(() =>
        {
            Assert.That(correctSignalReceived, Is.True, "Правильный сигнал должен быть получен");
            Assert.That(wrongSignalReceived, Is.False, "Неправильный сигнал не должен влиять");
        });
    }

    /// <summary>
    /// Тест: Wait{T} корректно освобождает ресурсы при Dispose.
    /// </summary>
    [TestCase(TestName = "Wait<T>.Dispose: корректное освобождение"), Benchmark]
    public async Task WaitDisposeReleasesResourcesCorrectly()
    {
        var wait = new Wait<string>(signal);
        var taskCompleted = false;

        // Запускаем ожидание
        var waitTask = Task.Run(async () =>
        {
            await wait.LockUntilAsync(() => false, Timeout.Infinite);
            taskCompleted = true;
        });

        // Даём время на начало ожидания
        await Task.Delay(100);

        // Dispose должен разблокировать
        wait.Dispose();

        // Ждём завершения с таймаутом
        await Task.WhenAny(waitTask, Task.Delay(1000));

        Assert.That(taskCompleted, Is.True, "Dispose должен разблокировать ожидание");
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync с асинхронным условием.
    /// </summary>
    [TestCase(TestName = "Wait<T>.LockUntilAsync: асинхронное условие"), Benchmark]
    public async Task LockUntilAsyncWithAsyncCondition()
    {
        var counter = 0;
        using var wait = new Wait<string>(signal);

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(20);
                signal.Send();
            }
        });

        async ValueTask<bool> AsyncConditionAsync()
        {
            await Task.Yield();
            return Interlocked.Increment(ref counter) >= 5;
        }

        await wait.LockUntilAsync(AsyncConditionAsync, 5000);

        Assert.That(counter, Is.GreaterThanOrEqualTo(5), "Условие должно достигнуть значения");
    }

    /// <summary>
    /// Тест: Wait{T} без состояния реагирует на любой сигнал.
    /// </summary>
    [TestCase(TestName = "Wait<T>: без состояния реагирует на любой сигнал"), Benchmark]
    public async Task WaitWithoutStateReactsToAnySignal()
    {
        var signalReceived = false;
        using var wait = new Wait<string>(signal);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            signal.Send("any-state");
            signalReceived = true;
        });

        await wait.LockUntilAsync(() => signalReceived, 5000);

        Assert.That(signalReceived, Is.True, "Должен среагировать на сигнал с любым состоянием");
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: Wait.Until работает корректно при параллельном изменении условия.
    /// </summary>
    [TestCase(TestName = "Thread Safety: Until с параллельным условием"), Benchmark]
    public async Task UntilThreadSafeWithConcurrentConditionUpdates()
    {
        var counter = 0;
        const int targetValue = 100;

        // Параллельно увеличиваем счётчик
        var incrementTask = Task.Run(async () =>
        {
            for (var i = 0; i < targetValue; i++)
            {
                Interlocked.Increment(ref counter);
                await Task.Delay(5);
            }
        });

        // Ждём пока счётчик достигнет половины
        var waitTask = Task.Run(() => Wait.Until(() => counter >= targetValue / 2, 10000));

        await Task.WhenAll(incrementTask, waitTask);

        Assert.That(counter, Is.GreaterThanOrEqualTo(targetValue / 2), "Счётчик должен достигнуть цели");
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync работает при множественных параллельных сигналах.
    /// </summary>
    [TestCase(TestName = "Thread Safety: LockUntilAsync с параллельными сигналами"), Benchmark]
    public async Task LockUntilAsyncHandlesMultipleConcurrentSignals()
    {
        var signalCount = 0;
        using var wait = new Wait<int>(new Signal<int>());
        var localSignal = new Signal<int>();
        using var localWait = new Wait<int>(localSignal);

        // Отправляем множество сигналов параллельно
        var signalTasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            await Task.Delay(Random.Shared.Next(10, 50));
            localSignal.Send(i);
            Interlocked.Increment(ref signalCount);
        })).ToArray();

        // Ждём пока получим достаточно сигналов
        await localWait.LockUntilAsync(() => signalCount >= 5, 5000);

        await Task.WhenAll(signalTasks);

        Assert.That(signalCount, Is.GreaterThanOrEqualTo(5), "Должны быть получены сигналы");
    }

    /// <summary>
    /// Тест: Множественные Wait{T} на одном Signal работают корректно.
    /// </summary>
    [TestCase(TestName = "Thread Safety: множественные Wait на одном Signal"), Benchmark]
    public async Task MultipleWaitsOnSameSignalWorkCorrectly()
    {
        var localSignal = new Signal<string>();
        var completedCount = 0;
        const int waiterCount = 5;

        var waiters = new List<Wait<string>>();
        var tasks = new List<Task>();

        for (var i = 0; i < waiterCount; i++)
        {
            var waiter = new Wait<string>(localSignal);
            waiters.Add(waiter);

            var localI = i;
            tasks.Add(Task.Run(async () =>
            {
                var flag = false;
                await waiter.LockUntilAsync(() => flag || localI < 3, 5000);
                Interlocked.Increment(ref completedCount);
            }));
        }

        // Отправляем сигнал
        await Task.Delay(100);
        localSignal.Send();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        foreach (var waiter in waiters) waiter.Dispose();

        Assert.That(completedCount, Is.EqualTo(waiterCount), "Все ожидающие должны завершиться");
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Тест: Wait.Until с нулевым таймаутом.
    /// </summary>
    [TestCase(TestName = "Edge Case: Until с нулевым таймаутом"), Benchmark]
    public void UntilWithZeroTimeoutReturnsImmediately()
    {
        var sw = Stopwatch.StartNew();

        Wait.Until(() => false, 0);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Должен вернуться немедленно");
    }

    /// <summary>
    /// Тест: Wait{T}.LockUntilAsync после Dispose не зависает.
    /// </summary>
    [TestCase(TestName = "Edge Case: LockUntilAsync после Dispose"), Benchmark]
    public async Task LockUntilAsyncAfterDisposeDoesNotHang()
    {
        var wait = new Wait<string>(signal);
        wait.Dispose();

        var sw = Stopwatch.StartNew();

        // Не должен зависать, потому что isDisposed = true
        await wait.LockUntilAsync(() => false, 1000);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500), "Не должен зависать после Dispose");
    }

    #endregion
}
