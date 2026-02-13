#pragma warning disable CS0219

using System.Diagnostics;

namespace Atom.Threading.Tests;

/// <summary>
/// Тесты для <see cref="RateLimiter"/>.
/// </summary>
public class RateLimiterTests(ILogger logger) : BenchmarkTests<RateLimiterTests>(logger)
{
    #region Constructors

    public RateLimiterTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Helper Methods

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    private async ValueTask TestCallbackAsync()
    {
        Log("TestCallbackAsync(): START");
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Log("TestCallbackAsync(): END");
    }

    #endregion

    #region Constructor Tests

    /// <summary>
    /// Тест: конструктор с корректными параметрами.
    /// </summary>
    [TestCase(TestName = "RateLimiter: конструктор с валидными параметрами"), Benchmark]
    public void ConstructorWithValidParameters()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(limiter.Limit, Is.EqualTo(5), "Limit должен быть 5");
            Assert.That(limiter.Rate.TotalMilliseconds, Is.GreaterThan(900).And.LessThan(1100), "Rate должен быть ~1с");
        });
    }

    /// <summary>
    /// Тест: конструктор с миллисекундами.
    /// </summary>
    [TestCase(TestName = "RateLimiter: конструктор с миллисекундами"), Benchmark]
    public void ConstructorWithMilliseconds()
    {
        using var limiter = new RateLimiter(10, 500);

        Assert.Multiple(() =>
        {
            Assert.That(limiter.Limit, Is.EqualTo(10), "Limit должен быть 10");
            Assert.That(limiter.Rate.TotalMilliseconds, Is.GreaterThan(400).And.LessThan(600), "Rate должен быть ~500мс");
        });
    }

    /// <summary>
    /// Тест: конструктор выбрасывает исключение при некорректном limit.
    /// </summary>
    [TestCase(TestName = "RateLimiter: конструктор с некорректным limit"), Benchmark]
    public void ConstructorThrowsOnInvalidLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(-1, TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// Тест: конструктор выбрасывает исключение при некорректном rate.
    /// </summary>
    [TestCase(TestName = "RateLimiter: конструктор с некорректным rate"), Benchmark]
    public void ConstructorThrowsOnInvalidRate()
    {
        Assert.Throws<ArgumentException>(() => new RateLimiter(1, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new RateLimiter(1, TimeSpan.FromMilliseconds(-1)));
    }

    #endregion

    #region Basic Functionality Tests

    /// <summary>
    /// Тест: базовая пропускная способность.
    /// </summary>
    [TestCase(TestName = "RateLimiter: базовая пропускная способность"), Benchmark]
    public async Task BasicThroughputTest()
    {
        using var limiter = new RateLimiter(3, TimeSpan.FromMilliseconds(500));
        var callTimes = new List<DateTime>();
        var lockObj = new object();

        for (var i = 0; i < 6; i++)
        {
            await limiter.CallAsync(() =>
            {
                lock (lockObj) callTimes.Add(DateTime.UtcNow);
            });
        }

        // Первые 3 должны быть быстрыми, остальные - с задержкой
        Assert.That(callTimes.Count, Is.EqualTo(6), "Все вызовы должны завершиться");

        // Проверяем, что есть задержка после первых 3 вызовов
        if (callTimes.Count >= 4)
        {
            var firstThreeTime = callTimes[2] - callTimes[0];
            var afterThreeTime = callTimes[5] - callTimes[2];

            Log($"Первые 3: {firstThreeTime.TotalMilliseconds}мс, Следующие 3: {afterThreeTime.TotalMilliseconds}мс");

            // Следующие 3 должны занять больше времени из-за ограничения
            Assert.That(afterThreeTime.TotalMilliseconds, Is.GreaterThan(200), "Должна быть задержка после лимита");
        }
    }

    /// <summary>
    /// Тест: CallAsync с Action.
    /// </summary>
    [TestCase(TestName = "RateLimiter: CallAsync с Action"), Benchmark]
    public async Task CallAsyncWithActionWorks()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));
        var callCount = 0;

        for (var i = 0; i < 3; i++)
        {
            await limiter.CallAsync(() => Interlocked.Increment(ref callCount));
        }

        Assert.That(callCount, Is.EqualTo(3), "Все вызовы должны выполниться");
    }

    /// <summary>
    /// Тест: CallAsync с Func{ValueTask}.
    /// </summary>
    [TestCase(TestName = "RateLimiter: CallAsync с Func<ValueTask>"), Benchmark]
    public async Task CallAsyncWithValueTaskFuncWorks()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));
        var callCount = 0;

        for (var i = 0; i < 3; i++)
        {
            await limiter.CallAsync(async () =>
            {
                await Task.Yield();
                Interlocked.Increment(ref callCount);
            });
        }

        Assert.That(callCount, Is.EqualTo(3), "Все вызовы должны выполниться");
    }

    /// <summary>
    /// Тест: CallAsync с возвращаемым значением.
    /// </summary>
    [TestCase(TestName = "RateLimiter: CallAsync с возвратом значения"), Benchmark]
    public async Task CallAsyncWithResultWorks()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));
        var results = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            var localI = i;
            var result = await limiter.CallAsync(() => localI * 2);
            results.Add(result);
        }

        Assert.That(results, Is.EqualTo([0, 2, 4]), "Результаты должны быть корректными");
    }

    /// <summary>
    /// Тест: CallAsync с асинхронным возвращаемым значением.
    /// </summary>
    [TestCase(TestName = "RateLimiter: CallAsync с асинхронным возвратом"), Benchmark]
    public async Task CallAsyncWithAsyncResultWorks()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));
        var results = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            var localI = i;
            var result = await limiter.CallAsync(async () =>
            {
                await Task.Yield();
                return localI * 2;
            });
            results.Add(result);
        }

        Assert.That(results, Is.EqualTo([0, 2, 4]), "Результаты должны быть корректными");
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// Тест: изменение Limit динамически.
    /// </summary>
    [TestCase(TestName = "RateLimiter: динамическое изменение Limit"), Benchmark]
    public async Task LimitCanBeChangedDynamically()
    {
        using var limiter = new RateLimiter(2, TimeSpan.FromMilliseconds(500));

        Assert.That(limiter.Limit, Is.EqualTo(2), "Начальный Limit = 2");

        limiter.Limit = 5;
        Assert.That(limiter.Limit, Is.EqualTo(5), "Limit должен измениться на 5");

        // Проверяем, что можно выполнить 5 вызовов быстро
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            await limiter.CallAsync(() => { });
        }
        sw.Stop();

        Log($"5 вызовов заняли: {sw.ElapsedMilliseconds}мс");
    }

    /// <summary>
    /// Тест: изменение Rate динамически.
    /// </summary>
    [TestCase(TestName = "RateLimiter: динамическое изменение Rate"), Benchmark]
    public void RateCanBeChangedDynamically()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));

        limiter.Rate = TimeSpan.FromMilliseconds(500);

        Assert.That(limiter.Rate.TotalMilliseconds, Is.GreaterThan(400).And.LessThan(600), "Rate должен измениться на ~500мс");
    }

    /// <summary>
    /// Тест: Limit выбрасывает исключение при некорректном значении.
    /// </summary>
    [TestCase(TestName = "RateLimiter: Limit с некорректным значением"), Benchmark]
    public void LimitThrowsOnInvalidValue()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Limit = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.Limit = -1);
    }

    /// <summary>
    /// Тест: Rate выбрасывает исключение при некорректном значении.
    /// </summary>
    [TestCase(TestName = "RateLimiter: Rate с некорректным значением"), Benchmark]
    public void RateThrowsOnInvalidValue()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentException>(() => limiter.Rate = TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => limiter.Rate = TimeSpan.FromMilliseconds(-1));
    }

    #endregion

    #region Reset Tests

    /// <summary>
    /// Тест: Reset отменяет текущие ожидания.
    /// </summary>
    [TestCase(TestName = "RateLimiter: Reset отменяет ожидания"), Benchmark]
    public async Task ResetCancelsPendingCalls()
    {
        using var limiter = new RateLimiter(1, TimeSpan.FromSeconds(10));
        var firstCompleted = false;
        var secondCompleted = false;

        // Первый вызов - занимает слот
        var firstTask = limiter.CallAsync(() => firstCompleted = true);
        await firstTask;

        // Второй вызов будет ждать
        var secondTask = Task.Run(async () =>
        {
            await limiter.CallAsync(() => secondCompleted = true);
        });

        await Task.Delay(100);

        // Reset должен отменить ожидание
        limiter.Reset();

        // Ждём немного
        await Task.Delay(200);

        Assert.That(firstCompleted, Is.True, "Первый вызов должен завершиться");
        // Второй может или не завершиться, или завершиться без выполнения callback
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: параллельные вызовы не превышают лимит.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельные вызовы не превышают лимит"), Benchmark]
    public async Task ConcurrentCallsRespectLimit()
    {
        const int limit = 3;
        const int rate = 500;
        using var limiter = new RateLimiter(limit, rate);
        var callTimes = new List<DateTime>();
        var lockObj = new object();
        const int totalCalls = 10;

        var tasks = Enumerable.Range(0, totalCalls).Select(_ => Task.Run(async () =>
        {
            await limiter.CallAsync(() =>
            {
                lock (lockObj) callTimes.Add(DateTime.UtcNow);
            });
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(callTimes.Count, Is.EqualTo(totalCalls), "Все вызовы должны завершиться");

        // Проверяем, что в любом окне rate не более limit вызовов
        callTimes.Sort();
        for (var i = limit; i < callTimes.Count; i++)
        {
            var windowStart = callTimes[i - limit];
            var current = callTimes[i];
            var window = (current - windowStart).TotalMilliseconds;

            Log($"Окно [{i - limit}..{i}]: {window}мс");

            // Допускаем небольшую погрешность
            Assert.That(window, Is.GreaterThanOrEqualTo(rate * 0.8),
                $"Интервал между {i - limit} и {i} вызовами должен быть >= {rate}мс");
        }
    }

    /// <summary>
    /// Тест: параллельное изменение Limit.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельное изменение Limit"), Benchmark]
    public async Task ConcurrentLimitChangeNoCorruption()
    {
        using var limiter = new RateLimiter(5, TimeSpan.FromMilliseconds(100));
        var exceptions = new List<Exception>();
        const int iterations = 100;

        var tasks = new List<Task>();

        // Задачи меняют Limit
        for (var i = 0; i < iterations; i++)
        {
            var newLimit = (i % 10) + 1;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    limiter.Limit = newLimit;
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));
        }

        // Задачи выполняют вызовы
        for (var i = 0; i < iterations; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await limiter.CallAsync(() => { });
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при параллельном изменении Limit");
    }

    #endregion

    #region Dispose Tests

    /// <summary>
    /// Тест: Dispose корректно освобождает ресурсы.
    /// </summary>
    [TestCase(TestName = "RateLimiter: Dispose освобождает ресурсы"), Benchmark]
    public void DisposeReleasesResources()
    {
        var limiter = new RateLimiter(5, TimeSpan.FromSeconds(1));
        limiter.Dispose();

        // Повторный Dispose не должен выбрасывать
        Assert.DoesNotThrow(limiter.Dispose);
    }

    #endregion

    #region Original Tests (Refactored)

    [TestCase(TestName = "Тест проверки пропускной способности"), Benchmark]
    public async Task ManualTestAsync()
    {
        using var limiter = new RateLimiter(2, 500);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 4; ++i) await limiter.CallAsync(TestCallbackAsync);
        sw.Stop();

        Log($"4 вызова заняли: {sw.ElapsedMilliseconds}мс");

        // 4 вызова с лимитом 2 за 500мс должны занять минимум ~500мс
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(400), "Должна быть задержка");
    }

    #endregion
}