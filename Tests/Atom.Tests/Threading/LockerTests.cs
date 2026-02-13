using System.Diagnostics;

namespace Atom.Threading.Tests;

/// <summary>
/// Тесты для <see cref="Locker"/>.
/// </summary>
public class LockerTests(ILogger logger) : BenchmarkTests<LockerTests>(logger)
{
    #region Constructors

    public LockerTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Helper Methods

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    #endregion

    #region Basic Functionality Tests

    /// <summary>
    /// Тест: Locker создаётся с корректными начальными значениями.
    /// </summary>
    [TestCase(TestName = "Locker: создание с начальным значением"), Benchmark]
    public void LockerCreatedWithCorrectInitialCount()
    {
        using var locker = new Locker(3, 5);

        Assert.That(locker.CurrentCount, Is.EqualTo(3), "CurrentCount должен быть равен initialCount");
    }

    /// <summary>
    /// Тест: Locker по умолчанию имеет count = 1.
    /// </summary>
    [TestCase(TestName = "Locker: конструктор по умолчанию"), Benchmark]
    public void DefaultLockerHasCountOne()
    {
        using var locker = new Locker();

        Assert.That(locker.CurrentCount, Is.EqualTo(1), "По умолчанию CurrentCount = 1");
    }

    /// <summary>
    /// Тест: Wait уменьшает CurrentCount.
    /// </summary>
    [TestCase(TestName = "Locker: Wait уменьшает CurrentCount"), Benchmark]
    public void WaitDecreasesCurrentCount()
    {
        using var locker = new Locker(2, 2);

        locker.Wait();

        Assert.That(locker.CurrentCount, Is.EqualTo(1), "После Wait CurrentCount должен уменьшиться");
    }

    /// <summary>
    /// Тест: Release увеличивает CurrentCount.
    /// </summary>
    [TestCase(TestName = "Locker: Release увеличивает CurrentCount"), Benchmark]
    public void ReleaseIncreasesCurrentCount()
    {
        using var locker = new Locker(0, 2);

        locker.Release();

        Assert.That(locker.CurrentCount, Is.EqualTo(1), "После Release CurrentCount должен увеличиться");
    }

    /// <summary>
    /// Тест: Release с количеством.
    /// </summary>
    [TestCase(TestName = "Locker: Release с количеством"), Benchmark]
    public void ReleaseWithCountIncreasesCurrentCount()
    {
        using var locker = new Locker(0, 5);

        locker.Release(3);

        Assert.That(locker.CurrentCount, Is.EqualTo(3), "После Release(3) CurrentCount должен быть 3");
    }

    #endregion

    #region Synchronous Wait Tests

    /// <summary>
    /// Тест: Wait блокирует поток, пока не будет вызван Release.
    /// </summary>
    [TestCase(TestName = "Locker: Wait блокирует до Release"), Benchmark]
    public async Task WaitBlocksUntilRelease()
    {
        using var locker = new Locker(0, 1);
        var waitCompleted = false;

        var waitTask = Task.Run(() =>
        {
            locker.Wait();
            waitCompleted = true;
        });

        // Даём время на начало ожидания
        await Task.Delay(100);
        Assert.That(waitCompleted, Is.False, "Wait должен заблокировать поток");

        // Освобождаем
        locker.Release();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(waitCompleted, Is.True, "Wait должен завершиться после Release");
    }

    /// <summary>
    /// Тест: Wait с таймаутом возвращает false при истечении.
    /// </summary>
    [TestCase(TestName = "Locker: Wait с таймаутом"), Benchmark]
    public void WaitWithTimeoutReturnsFalseOnTimeout()
    {
        using var locker = new Locker(0, 1);

        var sw = Stopwatch.StartNew();
        var result = locker.Wait(200);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Wait должен вернуть false по таймауту");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(180), "Должен ждать ~200мс");
        });
    }

    /// <summary>
    /// Тест: Wait с таймаутом возвращает true при успешном входе.
    /// </summary>
    [TestCase(TestName = "Locker: Wait с таймаутом успешный вход"), Benchmark]
    public void WaitWithTimeoutReturnsTrueOnSuccess()
    {
        using var locker = new Locker(1, 1);

        var result = locker.Wait(1000);

        Assert.That(result, Is.True, "Wait должен вернуть true при успешном входе");
    }

    /// <summary>
    /// Тест: Wait с CancellationToken реагирует на отмену.
    /// </summary>
    [TestCase(TestName = "Locker: Wait с CancellationToken"), Benchmark]
    public void WaitWithCancellationTokenThrowsOnCancellation()
    {
        using var locker = new Locker(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.Throws<OperationCanceledException>(() => locker.Wait(cts.Token));
    }

    /// <summary>
    /// Тест: Wait с TimeSpan таймаутом.
    /// </summary>
    [TestCase(TestName = "Locker: Wait с TimeSpan"), Benchmark]
    public void WaitWithTimeSpanTimeout()
    {
        using var locker = new Locker(0, 1);

        var result = locker.Wait(TimeSpan.FromMilliseconds(200));

        Assert.That(result, Is.False, "Wait должен вернуть false по таймауту");
    }

    #endregion

    #region Asynchronous WaitAsync Tests

    /// <summary>
    /// Тест: WaitAsync ожидает асинхронно.
    /// </summary>
    [TestCase(TestName = "Locker: WaitAsync базовая работа"), Benchmark]
    public async Task WaitAsyncWaitsForRelease()
    {
        using var locker = new Locker(0, 1);
        var waitCompleted = false;

        var waitTask = Task.Run(async () =>
        {
            await locker.WaitAsync();
            waitCompleted = true;
        });

        await Task.Delay(100);
        Assert.That(waitCompleted, Is.False, "WaitAsync должен ожидать");

        locker.Release();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(waitCompleted, Is.True, "WaitAsync должен завершиться после Release");
    }

    /// <summary>
    /// Тест: WaitAsync с таймаутом.
    /// </summary>
    [TestCase(TestName = "Locker: WaitAsync с таймаутом"), Benchmark]
    public async Task WaitAsyncWithTimeoutReturnsFalseOnTimeout()
    {
        using var locker = new Locker(0, 1);

        var sw = Stopwatch.StartNew();
        var result = await locker.WaitAsync(200);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "WaitAsync должен вернуть false по таймауту");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(180), "Должен ждать ~200мс");
        });
    }

    /// <summary>
    /// Тест: WaitAsync с CancellationToken реагирует на отмену.
    /// </summary>
    [TestCase(TestName = "Locker: WaitAsync с CancellationToken"), Benchmark]
    [CancelAfter(5000)]
    public async Task WaitAsyncWithCancellationTokenThrowsOnCancellation()
    {
        using var locker = new Locker(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThatAsync(
            async () => await locker.WaitAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>()
        );
    }

    /// <summary>
    /// Тест: WaitAsync с таймаутом и CancellationToken.
    /// </summary>
    [TestCase(TestName = "Locker: WaitAsync с таймаутом и CancellationToken"), Benchmark]
    [CancelAfter(5000)]
    public async Task WaitAsyncWithTimeoutAndCancellationToken()
    {
        using var locker = new Locker(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Токен отменится раньше таймаута
        await Assert.ThatAsync(
            async () => await locker.WaitAsync(5000, cts.Token),
            Throws.InstanceOf<OperationCanceledException>()
        );
    }

    /// <summary>
    /// Тест: WaitAsync с TimeSpan.
    /// </summary>
    [TestCase(TestName = "Locker: WaitAsync с TimeSpan"), Benchmark]
    public async Task WaitAsyncWithTimeSpan()
    {
        using var locker = new Locker(0, 1);

        var result = await locker.WaitAsync(TimeSpan.FromMilliseconds(200));

        Assert.That(result, Is.False, "WaitAsync должен вернуть false по таймауту");
    }

    #endregion

    #region Event Tests

    /// <summary>
    /// Тест: событие Waiting вызывается при входе в ожидание.
    /// </summary>
    [TestCase(TestName = "Locker: событие Waiting"), Benchmark]
    public void WaitingEventFired()
    {
        using var locker = new Locker(1, 1);
        var eventFired = false;

        locker.Waiting += (_, _) => eventFired = true;

        locker.Wait();

        Assert.That(eventFired, Is.True, "Событие Waiting должно быть вызвано");
    }

    /// <summary>
    /// Тест: событие Released вызывается при освобождении.
    /// </summary>
    [TestCase(TestName = "Locker: событие Released"), Benchmark]
    public void ReleasedEventFired()
    {
        using var locker = new Locker(0, 3);
        var releasedCount = 0;

        locker.Released += (_, args) => releasedCount = args.ReleaseCount;

        locker.Release(2);

        Assert.That(releasedCount, Is.EqualTo(2), "Released должен передать количество освобождённых");
    }

    /// <summary>
    /// Тест: события Waiting и Released работают с WaitAsync.
    /// </summary>
    [TestCase(TestName = "Locker: события с WaitAsync"), Benchmark]
    public async Task EventsWorkWithWaitAsync()
    {
        using var locker = new Locker(1, 1);
        var waitingFired = false;
        var releasedFired = false;

        locker.Waiting += (_, _) => waitingFired = true;
        locker.Released += (_, _) => releasedFired = true;

        await locker.WaitAsync();
        locker.Release();

        Assert.Multiple(() =>
        {
            Assert.That(waitingFired, Is.True, "Waiting должен быть вызван");
            Assert.That(releasedFired, Is.True, "Released должен быть вызван");
        });
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: параллельные Wait и Release не приводят к deadlock.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельные Wait/Release"), Benchmark]
    public async Task ConcurrentWaitAndReleaseNoDeadlock()
    {
        using var locker = new Locker(5, 10);
        var completedCount = 0;
        const int iterations = 100;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(async () =>
        {
            // Чётные - захватывают и освобождают
            if (i % 2 == 0)
            {
                if (locker.Wait(100))
                {
                    Interlocked.Increment(ref completedCount);
                    await Task.Delay(5);
                    locker.Release();
                }
            }
            else
            {
                // Нечётные просто ждут небольшую задержку
                await Task.Delay(10);
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(cts.Token);

        Log($"Завершено Wait: {completedCount}");
        Assert.Pass("Параллельные Wait/Release завершились без deadlock");
    }

    /// <summary>
    /// Тест: множественные потоки могут входить до лимита.
    /// </summary>
    [TestCase(TestName = "Thread Safety: множественные потоки входят до лимита"), Benchmark]
    public async Task MultipleThreadsCanEnterUpToLimit()
    {
        const int limit = 3;
        using var locker = new Locker(limit, limit);
        var insideCount = 0;
        var maxInsideCount = 0;
        using var barrier = new Barrier(limit);

        var tasks = Enumerable.Range(0, limit).Select(i => Task.Run(() =>
        {
            locker.Wait();
            var current = Interlocked.Increment(ref insideCount);
            Interlocked.CompareExchange(ref maxInsideCount, current, Math.Min(current - 1, maxInsideCount));

            barrier.SignalAndWait(TimeSpan.FromSeconds(5));

            Interlocked.Decrement(ref insideCount);
            locker.Release();
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(maxInsideCount, Is.LessThanOrEqualTo(limit), $"Не более {limit} потоков должны быть внутри");
    }

    /// <summary>
    /// Тест: параллельные WaitAsync.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельные WaitAsync"), Benchmark]
    public async Task ConcurrentWaitAsyncWorks()
    {
        using var locker = new Locker(5, 10);
        var completedCount = 0;
        const int taskCount = 20;

        var tasks = Enumerable.Range(0, taskCount).Select(async _ =>
        {
            if (await locker.WaitAsync(500))
            {
                Interlocked.Increment(ref completedCount);
                await Task.Delay(10);
                locker.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Log($"Завершено WaitAsync: {completedCount}");
        Assert.That(completedCount, Is.GreaterThan(0), "Хотя бы некоторые потоки должны завершиться");
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Тест: Release без предварительного Wait.
    /// </summary>
    [TestCase(TestName = "Edge Case: Release при достижении maxCount"), Benchmark]
    public void ReleaseAtMaxCountThrows()
    {
        using var locker = new Locker(2, 2);

        Assert.Throws<SemaphoreFullException>(locker.Release);
    }

    /// <summary>
    /// Тест: AvailableWaitHandle доступен.
    /// </summary>
    [TestCase(TestName = "Locker: AvailableWaitHandle доступен"), Benchmark]
    public void AvailableWaitHandleIsAccessible()
    {
        using var locker = new Locker(1, 1);

        Assert.That(locker.AvailableWaitHandle, Is.Not.Null, "AvailableWaitHandle должен быть доступен");
    }

    /// <summary>
    /// Тест: ToString возвращает строку.
    /// </summary>
    [TestCase(TestName = "Locker: ToString"), Benchmark]
    public void ToStringReturnsString()
    {
        using var locker = new Locker(1, 1);

        var result = locker.ToString();

        Assert.That(result, Is.Not.Null.And.Not.Empty, "ToString должен вернуть непустую строку");
    }

    /// <summary>
    /// Тест: Dispose освобождает ресурсы.
    /// </summary>
    [TestCase(TestName = "Locker: Dispose"), Benchmark]
    public void DisposeReleasesResources()
    {
        var locker = new Locker(1, 1);
        locker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => locker.Wait(0));
    }

    /// <summary>
    /// Тест: Wait с нулевым таймаутом возвращается немедленно.
    /// </summary>
    [TestCase(TestName = "Edge Case: Wait с нулевым таймаутом"), Benchmark]
    public void WaitWithZeroTimeoutReturnsImmediately()
    {
        using var locker = new Locker(0, 1);

        var sw = Stopwatch.StartNew();
        var result = locker.Wait(0);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False, "Wait(0) должен вернуть false если нет слотов");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Должен вернуться немедленно");
        });
    }

    #endregion

    #region Stress Tests

    /// <summary>
    /// Стресс-тест: высокая нагрузка Wait/Release.
    /// </summary>
    [TestCase(TestName = "Stress: высокая нагрузка Wait/Release"), Benchmark]
    public async Task HighLoadWaitRelease()
    {
        using var locker = new Locker(10, 100);
        var operationsCompleted = 0;
        const int iterations = 1000;
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(async () =>
        {
            try
            {
                if (locker.Wait(10))
                {
                    await Task.Yield();
                    locker.Release();
                }
                Interlocked.Increment(ref operationsCompleted);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при высокой нагрузке");
        Log($"Операций завершено: {operationsCompleted}");
    }

    #endregion
}
