using System.Collections.Concurrent;

namespace Atom.Threading.Tests;

/// <summary>
/// Тесты для <see cref="ResettableCancellationTokenSource"/>.
/// </summary>
public class ResettableCancellationTokenSourceTests(ILogger logger) : BenchmarkTests<ResettableCancellationTokenSourceTests>(logger)
{
    #region Constructors

    public ResettableCancellationTokenSourceTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Constructor Tests

    /// <summary>
    /// Тест: конструктор по умолчанию создаёт не отменённый источник.
    /// </summary>
    [TestCase(TestName = "RCTS: конструктор по умолчанию"), Benchmark]
    public void DefaultConstructorCreatesNotCancelledSource()
    {
        using var cts = new ResettableCancellationTokenSource();

        Assert.Multiple(() =>
        {
            Assert.That(cts.IsCancellationRequested, Is.False, "IsCancellationRequested должен быть false");
            Assert.That(cts.Token.IsCancellationRequested, Is.False, "Token.IsCancellationRequested должен быть false");
        });
    }

    /// <summary>
    /// Тест: конструктор с задержкой.
    /// </summary>
    [TestCase(TestName = "RCTS: конструктор с задержкой"), Benchmark]
    public async Task ConstructorWithDelayAutoCancelsAfterDelay()
    {
        using var cts = new ResettableCancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.That(cts.IsCancellationRequested, Is.False, "Сразу после создания не должен быть отменён");

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    /// <summary>
    /// Тест: конструктор с миллисекундами.
    /// </summary>
    [TestCase(TestName = "RCTS: конструктор с миллисекундами"), Benchmark]
    public async Task ConstructorWithMillisecondsDelay()
    {
        using var cts = new ResettableCancellationTokenSource(200);

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    #endregion

    #region Token Property Tests

    /// <summary>
    /// Тест: Token возвращает актуальный токен.
    /// </summary>
    [TestCase(TestName = "RCTS: Token возвращает актуальный токен"), Benchmark]
    public void TokenReturnsCurrentToken()
    {
        using var cts = new ResettableCancellationTokenSource();

        var token = cts.Token;

        Assert.That(token.CanBeCanceled, Is.True, "Token должен поддерживать отмену");
    }

    /// <summary>
    /// Тест: Token обновляется после Reset.
    /// </summary>
    [TestCase(TestName = "RCTS: Token обновляется после Reset"), Benchmark]
    public void TokenUpdatesAfterReset()
    {
        using var cts = new ResettableCancellationTokenSource();

        var token1 = cts.Token;
        cts.Cancel();
        cts.Reset();
        var token2 = cts.Token;

        Assert.Multiple(() =>
        {
            Assert.That(token1.IsCancellationRequested, Is.True, "Старый токен должен быть отменён");
            Assert.That(token2.IsCancellationRequested, Is.False, "Новый токен не должен быть отменён");
        });
    }

    #endregion

    #region Cancel Tests

    /// <summary>
    /// Тест: Cancel отменяет токен.
    /// </summary>
    [TestCase(TestName = "RCTS: Cancel отменяет токен"), Benchmark]
    public void CancelCancelsToken()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.Cancel();

        Assert.That(cts.IsCancellationRequested, Is.True, "IsCancellationRequested должен быть true");
    }

    /// <summary>
    /// Тест: Cancel с throwOnFirstException.
    /// </summary>
    [TestCase(TestName = "RCTS: Cancel с throwOnFirstException"), Benchmark]
    public void CancelWithThrowOnFirstException()
    {
        using var cts = new ResettableCancellationTokenSource();

        // Регистрируем обработчик, который бросает исключение
        cts.Token.Register(() => throw new InvalidOperationException("Test"));

        Assert.Throws<InvalidOperationException>(() => cts.Cancel(throwOnFirstException: true));
    }

    /// <summary>
    /// Тест: CancelAsync отменяет токен.
    /// </summary>
    [TestCase(TestName = "RCTS: CancelAsync отменяет токен"), Benchmark]
    public async Task CancelAsyncCancelsToken()
    {
        using var cts = new ResettableCancellationTokenSource();

        await cts.CancelAsync();

        Assert.That(cts.IsCancellationRequested, Is.True, "IsCancellationRequested должен быть true");
    }

    /// <summary>
    /// Тест: CancelAfter с TimeSpan.
    /// </summary>
    [TestCase(TestName = "RCTS: CancelAfter с TimeSpan"), Benchmark]
    public async Task CancelAfterWithTimeSpan()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        Assert.That(cts.IsCancellationRequested, Is.False, "Сразу не должен быть отменён");

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    /// <summary>
    /// Тест: CancelAfter с миллисекундами.
    /// </summary>
    [TestCase(TestName = "RCTS: CancelAfter с миллисекундами"), Benchmark]
    public async Task CancelAfterWithMilliseconds()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.CancelAfter(200);

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    #endregion

    #region Reset Tests

    /// <summary>
    /// Тест: Reset сбрасывает отменённый токен.
    /// </summary>
    [TestCase(TestName = "RCTS: Reset сбрасывает отменённый токен"), Benchmark]
    public void ResetResetsСancelledToken()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.Cancel();
        Assert.That(cts.IsCancellationRequested, Is.True, "После Cancel должен быть отменён");

        cts.Reset();
        Assert.That(cts.IsCancellationRequested, Is.False, "После Reset не должен быть отменён");
    }

    /// <summary>
    /// Тест: Reset с задержкой создаёт новый таймер.
    /// </summary>
    [TestCase(TestName = "RCTS: Reset с задержкой"), Benchmark]
    public async Task ResetWithDelayCreatesNewTimer()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.Cancel();
        cts.Reset(TimeSpan.FromMilliseconds(200));

        Assert.That(cts.IsCancellationRequested, Is.False, "После Reset не должен быть отменён");

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    /// <summary>
    /// Тест: Reset с миллисекундами.
    /// </summary>
    [TestCase(TestName = "RCTS: Reset с миллисекундами"), Benchmark]
    public async Task ResetWithMilliseconds()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.Cancel();
        cts.Reset(200);

        Assert.That(cts.IsCancellationRequested, Is.False, "После Reset не должен быть отменён");

        await Task.Delay(300);

        Assert.That(cts.IsCancellationRequested, Is.True, "После задержки должен быть отменён");
    }

    /// <summary>
    /// Тест: множественные Reset.
    /// </summary>
    [TestCase(TestName = "RCTS: множественные Reset"), Benchmark]
    public void MultipleResetsWork()
    {
        using var cts = new ResettableCancellationTokenSource();

        for (var i = 0; i < 10; i++)
        {
            cts.Cancel();
            Assert.That(cts.IsCancellationRequested, Is.True, $"Итерация {i}: После Cancel должен быть отменён");

            cts.Reset();
            Assert.That(cts.IsCancellationRequested, Is.False, $"Итерация {i}: После Reset не должен быть отменён");
        }
    }

    #endregion

    #region TryReset Tests

    /// <summary>
    /// Тест: TryReset возвращает false если отменён.
    /// </summary>
    [TestCase(TestName = "RCTS: TryReset возвращает false если отменён"), Benchmark]
    public void TryResetReturnsFalseIfCancelled()
    {
        using var cts = new ResettableCancellationTokenSource();

        cts.Cancel();
        var result = cts.TryReset();

        Assert.That(result, Is.False, "TryReset должен вернуть false для отменённого токена");
    }

    /// <summary>
    /// Тест: TryReset возвращает true если не отменён.
    /// </summary>
    [TestCase(TestName = "RCTS: TryReset возвращает true если не отменён"), Benchmark]
    public void TryResetReturnsTrueIfNotCancelled()
    {
        using var cts = new ResettableCancellationTokenSource();

        var result = cts.TryReset();

        Assert.That(result, Is.True, "TryReset должен вернуть true для не отменённого токена");
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: параллельные Cancel и Reset.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельные Cancel и Reset"), Benchmark]
    public async Task ConcurrentCancelAndResetNoCorruption()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new List<Exception>();
        const int iterations = 100;

        var cancelTasks = Enumerable.Range(0, iterations).Select(_ => Task.Run(() =>
        {
            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        })).ToArray();

        var resetTasks = Enumerable.Range(0, iterations).Select(_ => Task.Run(() =>
        {
            try
            {
                cts.Reset();
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(cancelTasks.Concat(resetTasks)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при параллельных операциях");
    }

    /// <summary>
    /// Тест: параллельный доступ к Token.
    /// </summary>
    [TestCase(TestName = "Thread Safety: параллельный доступ к Token"), Benchmark]
    public async Task ConcurrentTokenAccessNoCorruption()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new List<Exception>();
        const int iterations = 100;

        var tasks = new List<Task>();

        for (var i = 0; i < iterations; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var token = cts.Token;
                    _ = token.IsCancellationRequested;
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));

            if (i % 10 == 0)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        cts.Reset();
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) exceptions.Add(ex);
                    }
                }));
            }
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(exceptions, Is.Empty, "Не должно быть исключений при параллельном доступе к Token");
    }

    #endregion

    #region Integration Tests

    /// <summary>
    /// Тест: использование с Task.Delay.
    /// </summary>
    [TestCase(TestName = "Integration: использование с Task.Delay"), Benchmark]
    [CancelAfter(5000)]
    public async Task UsageWithTaskDelay()
    {
        using var cts = new ResettableCancellationTokenSource();

        var taskCompleted = false;
        var taskCancelled = false;

        var delayTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, cts.Token);
                taskCompleted = true;
            }
            catch (OperationCanceledException)
            {
                taskCancelled = true;
            }
        });

        await Task.Delay(100);
        cts.Cancel();

        await delayTask;

        Assert.Multiple(() =>
        {
            Assert.That(taskCompleted, Is.False, "Задача не должна завершиться");
            Assert.That(taskCancelled, Is.True, "Задача должна быть отменена");
        });
    }

    /// <summary>
    /// Тест: Reset во время ожидания.
    /// </summary>
    [TestCase(TestName = "Integration: Reset во время ожидания"), Benchmark]
    public async Task ResetDuringWait()
    {
        using var cts = new ResettableCancellationTokenSource();

        var firstCancelled = false;
        var secondCancelled = false;

        // Первая задача использует токен
        var token1 = cts.Token;
        var task1 = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, token1);
            }
            catch (OperationCanceledException)
            {
                firstCancelled = true;
            }
        });

        await Task.Delay(100);

        // Отменяем и сбрасываем
        cts.Cancel();
        cts.Reset();

        await task1;

        // Вторая задача использует новый токен
        var token2 = cts.Token;
        var task2 = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100, token2);
            }
            catch (OperationCanceledException)
            {
                secondCancelled = true;
            }
        });

        await task2;

        Assert.Multiple(() =>
        {
            Assert.That(firstCancelled, Is.True, "Первая задача должна быть отменена");
            Assert.That(secondCancelled, Is.False, "Вторая задача не должна быть отменена");
        });
    }

    /// <summary>
    /// Тест: использование в цикле с переиспользованием.
    /// </summary>
    [TestCase(TestName = "Integration: переиспользование в цикле"), Benchmark]
    public async Task ReuseInLoop()
    {
        using var cts = new ResettableCancellationTokenSource();
        var completedCount = 0;

        for (var i = 0; i < 5; i++)
        {
            var taskCompleted = false;

            var token = cts.Token;
            var task = Task.Run(async () =>
            {
                await Task.Delay(50, token);
                taskCompleted = true;
            });

            await task;

            if (taskCompleted)
                completedCount++;

            // Сбрасываем для следующей итерации
            cts.Reset();
        }

        Assert.That(completedCount, Is.EqualTo(5), "Все итерации должны завершиться");
    }

    #endregion

    #region Race Condition Tests

    /// <summary>
    /// Тест: race condition при одновременном Cancel и доступе к Token.
    /// Проверяет, что не возникает ObjectDisposedException при параллельном Cancel/Reset и чтении Token.
    /// </summary>
    [TestCase(TestName = "Race Condition: Cancel и доступ к Token"), Benchmark]
    [CancelAfter(30_000)]
    public async Task RaceConditionCancelAndTokenAccessAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var operationCount = 0;
        const int duration = 3000;

        using var stopCts = new CancellationTokenSource(duration);

        // Потоки, которые постоянно читают Token
        var tokenReaders = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    var token = cts.Token;
                    var isCancelled = token.IsCancellationRequested;
                    Interlocked.Increment(ref operationCount);
                }
                catch (ObjectDisposedException ex)
                {
                    exceptions.Add(ex);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }

                await Task.Yield();
            }
        })).ToArray();

        // Потоки, которые постоянно Cancel и Reset
        var modifiers = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    cts.Cancel();
                    cts.Reset();
                    Interlocked.Increment(ref operationCount);
                }
                catch (ObjectDisposedException ex)
                {
                    exceptions.Add(ex);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }

                await Task.Yield();
            }
        })).ToArray();

        await Task.WhenAll(tokenReaders.Concat(modifiers));

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
            Assert.That(operationCount, Is.GreaterThan(0), "Должны быть выполнены операции");
        });
    }

    /// <summary>
    /// Тест: race condition при одновременном CancelAfter и Reset.
    /// </summary>
    [TestCase(TestName = "Race Condition: CancelAfter и Reset"), Benchmark]
    [CancelAfter(30_000)]
    public async Task RaceConditionCancelAfterAndResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var operationCount = 0;
        const int duration = 3000;

        using var stopCts = new CancellationTokenSource(duration);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        cts.CancelAfter(10);
                    }
                    else
                    {
                        cts.Reset();
                    }

                    Interlocked.Increment(ref operationCount);
                }
                catch (ObjectDisposedException ex)
                {
                    exceptions.Add(ex);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }

                await Task.Yield();
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(exceptions, Is.Empty,
            $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
    }

    /// <summary>
    /// Тест: race condition при Dispose во время активных операций.
    /// Проверяет, что операции не вызывают неожиданных исключений (NullReferenceException и т.д.).
    /// ObjectDisposedException — ожидаемое поведение после Dispose.
    /// </summary>
    [TestCase(TestName = "Race Condition: Dispose во время операций"), Benchmark]
    [CancelAfter(30_000)]
    public async Task RaceConditionDisposeDuringOperationsAsync()
    {
        // Собираем только неожиданные исключения (не ObjectDisposedException)
        var unexpectedExceptions = new ConcurrentBag<Exception>();

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var cts = new ResettableCancellationTokenSource();
            using var barrier = new Barrier(4);

            var tasks = new[]
            {
                Task.Run(() =>
                {
                    barrier.SignalAndWait(1000);
                    // ObjectDisposedException — ожидаемое поведение после Dispose
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { /* OK */ }
                    catch (Exception ex) { unexpectedExceptions.Add(ex); }
                }),
                Task.Run(() =>
                {
                    barrier.SignalAndWait(1000);
                    try { cts.Reset(); }
                    catch (ObjectDisposedException) { /* OK */ }
                    catch (Exception ex) { unexpectedExceptions.Add(ex); }
                }),
                Task.Run(() =>
                {
                    barrier.SignalAndWait(1000);
                    try { _ = cts.Token; }
                    catch (ObjectDisposedException) { /* OK */ }
                    catch (Exception ex) { unexpectedExceptions.Add(ex); }
                }),
                Task.Run(() =>
                {
                    barrier.SignalAndWait(1000);
                    cts.Dispose();
                })
            };

            await Task.WhenAll(tasks);
        }

        Assert.That(unexpectedExceptions, Is.Empty,
            $"Обнаружено {unexpectedExceptions.Count} неожиданных исключений (не ObjectDisposedException): " +
            string.Join(", ", unexpectedExceptions.Select(e => e.GetType().Name + ": " + e.Message)));
    }

    #endregion

    #region Stress Tests

    /// <summary>
    /// Стресс-тест: интенсивное использование Cancel/Reset.
    /// </summary>
    [TestCase(TestName = "Stress: интенсивные Cancel/Reset"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressIntensiveCancelResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var cancelCount = 0;
        var resetCount = 0;
        const int iterations = 100_000; // 10x увеличено

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() => // 2x потоков
        {
            for (var j = 0; j < iterations / 100; j++)
            {
                try
                {
                    if (j % 2 == 0)
                    {
                        cts.Cancel();
                        Interlocked.Increment(ref cancelCount);
                    }
                    else
                    {
                        cts.Reset();
                        Interlocked.Increment(ref resetCount);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.Message))}");
            Assert.That(cancelCount + resetCount, Is.EqualTo(iterations), "Все операции должны выполниться");
        });
    }

    /// <summary>
    /// Стресс-тест: множественные потоки с разными операциями.
    /// </summary>
    [TestCase(TestName = "Stress: множественные потоки с операциями"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressMultipleThreadsVariousOperationsAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var operations = new ConcurrentDictionary<string, int>();
        const int threadCount = 200; // 2x потоков
        const int iterationsPerThread = 1000; // 2x итераций

        var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(() =>
        {
            var random = new Random(i);

            for (var j = 0; j < iterationsPerThread; j++)
            {
                try
                {
                    var op = random.Next(6);
                    switch (op)
                    {
                        case 0:
                            cts.Cancel();
                            operations.AddOrUpdate("Cancel", 1, (_, v) => v + 1);
                            break;
                        case 1:
                            cts.Reset();
                            operations.AddOrUpdate("Reset", 1, (_, v) => v + 1);
                            break;
                        case 2:
                            _ = cts.Token;
                            operations.AddOrUpdate("Token", 1, (_, v) => v + 1);
                            break;
                        case 3:
                            _ = cts.IsCancellationRequested;
                            operations.AddOrUpdate("IsCancellationRequested", 1, (_, v) => v + 1);
                            break;
                        case 4:
                            cts.CancelAfter(1000);
                            operations.AddOrUpdate("CancelAfter", 1, (_, v) => v + 1);
                            break;
                        case 5:
                            cts.TryReset();
                            operations.AddOrUpdate("TryReset", 1, (_, v) => v + 1);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var totalOps = operations.Values.Sum();

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
            Assert.That(totalOps, Is.EqualTo(threadCount * iterationsPerThread), "Все операции должны выполниться");
        });
    }

    /// <summary>
    /// Стресс-тест: быстрое создание и уничтожение с параллельным доступом.
    /// </summary>
    [TestCase(TestName = "Stress: создание/уничтожение с доступом"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressCreateDisposeWithAccessAsync()
    {
        var exceptions = new ConcurrentBag<Exception>();
        var successfulIterations = 0;
        const int iterations = 1000; // 2x увеличено

        for (var i = 0; i < iterations; i++)
        {
            var cts = new ResettableCancellationTokenSource();
            var disposed = false;

            var accessTasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => // 2x потоков
            {
                for (var j = 0; j < 100 && !Volatile.Read(ref disposed); j++) // 2x итераций
                {
                    try
                    {
                        var token = cts.Token;
                        var isCancelled = token.IsCancellationRequested;
                        cts.Cancel();
                        cts.Reset();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ожидаемо после Dispose — НЕ добавляем в exceptions
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })).ToArray();

            await Task.Delay(10); // Даём потокам поработать
            Volatile.Write(ref disposed, true);
            cts.Dispose();

            await Task.WhenAll(accessTasks);
            Interlocked.Increment(ref successfulIterations);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Неожиданные исключения: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
            Assert.That(successfulIterations, Is.EqualTo(iterations), "Все итерации должны завершиться");
        });
    }

    /// <summary>
    /// Стресс-тест: использование Token.Register с параллельными Reset.
    /// </summary>
    [TestCase(TestName = "Stress: Token.Register с Reset"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressTokenRegisterWithResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var callbackCount = 0;
        const int iterations = 5000; // 5x увеличено

        var registerTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() => // 2.5x потоков
        {
            for (var i = 0; i < iterations / 50; i++)
            {
                try
                {
                    var token = cts.Token;
                    using var registration = token.Register(() => Interlocked.Increment(ref callbackCount));
                }
                catch (ObjectDisposedException)
                {
                    // Может произойти если CTS был сброшен
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        var resetTasks = Enumerable.Range(0, 25).Select(_ => Task.Run(() => // 2.5x потоков
        {
            for (var i = 0; i < iterations / 25; i++)
            {
                try
                {
                    cts.Cancel();
                    cts.Reset();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(registerTasks.Concat(resetTasks));

        Assert.That(exceptions, Is.Empty,
            $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.Message))}");
    }

    /// <summary>
    /// Стресс-тест: конкурентный CancelAsync.
    /// </summary>
    [TestCase(TestName = "Stress: конкурентный CancelAsync"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressConcurrentCancelAsyncAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var cancelCount = 0;
        const int iterations = 5000; // 10x увеличено

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () => // 2x потоков
        {
            for (var i = 0; i < iterations / 100; i++)
            {
                try
                {
                    await cts.CancelAsync();
                    Interlocked.Increment(ref cancelCount);
                    cts.Reset();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.Message))}");
            Assert.That(cancelCount, Is.EqualTo(iterations), "Все CancelAsync должны выполниться");
        });
    }

    /// <summary>
    /// Стресс-тест: экстремально быстрый Reset без пауз (tight loop).
    /// </summary>
    [TestCase(TestName = "Stress: tight loop Reset"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressTightLoopResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var resetCount = 0;
        const int iterations = 50_000;

        // Все потоки делают только Reset — максимальная конкуренция
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations / 100; i++)
            {
                try
                {
                    cts.Reset();
                    Interlocked.Increment(ref resetCount);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.Message))}");
            Assert.That(resetCount, Is.EqualTo(iterations), "Все Reset должны выполниться");
        });
    }

    /// <summary>
    /// Стресс-тест: параллельный доступ к Token во время Reset.
    /// </summary>
    [TestCase(TestName = "Stress: Token access во время Reset"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressTokenAccessDuringResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        var tokenAccessCount = 0;
        var resetCount = 0;
        const int duration = 5000; // 5 секунд

        using var stopCts = new CancellationTokenSource();
        using var startedEvent = new ManualResetEventSlim(false);
        var startedTasks = 0;
        const int taskCount = 20;

        var tokenTasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(() =>
        {
            Interlocked.Increment(ref startedTasks);
            startedEvent.Wait(); // Ждём сигнала старта
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    var token = cts.Token;
                    var isCancelled = cts.IsCancellationRequested;
                    Interlocked.Increment(ref tokenAccessCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        var resetTasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(() =>
        {
            Interlocked.Increment(ref startedTasks);
            startedEvent.Wait(); // Ждём сигнала старта
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    cts.Reset();
                    Interlocked.Increment(ref resetCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        // Ждём пока все задачи стартуют
        while (startedTasks < taskCount * 2) await Task.Delay(1);

        // Запускаем все задачи одновременно
        startedEvent.Set();

        // Даём поработать
        await Task.Delay(duration);
        stopCts.Cancel();

        await Task.WhenAll(tokenTasks.Concat(resetTasks));

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty,
                $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
            Assert.That(tokenAccessCount, Is.GreaterThan(0), "Должны быть успешные доступы к Token");
            Assert.That(resetCount, Is.GreaterThan(0), "Должны быть успешные Reset");
        });
    }

    /// <summary>
    /// Стресс-тест: CancelAfter с быстрыми Reset (гонка таймеров).
    /// </summary>
    [TestCase(TestName = "Stress: CancelAfter с быстрыми Reset"), Benchmark]
    [CancelAfter(120_000)]
    public async Task StressCancelAfterWithFastResetAsync()
    {
        using var cts = new ResettableCancellationTokenSource();
        var exceptions = new ConcurrentBag<Exception>();
        const int duration = 5000;

        using var stopCts = new CancellationTokenSource(duration);

        var cancelAfterTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    cts.CancelAfter(1); // Минимальная задержка — максимальная гонка
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        var resetTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            while (!stopCts.IsCancellationRequested)
            {
                try
                {
                    cts.Reset();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(cancelAfterTasks.Concat(resetTasks));

        Assert.That(exceptions, Is.Empty,
            $"Обнаружено {exceptions.Count} исключений: {string.Join(", ", exceptions.Take(5).Select(e => e.GetType().Name + ": " + e.Message))}");
    }

    #endregion

    #region Dispose Tests

    /// <summary>
    /// Тест: Dispose освобождает ресурсы.
    /// </summary>
    [TestCase(TestName = "RCTS: Dispose освобождает ресурсы"), Benchmark]
    public void DisposeReleasesResources()
    {
        var cts = new ResettableCancellationTokenSource();
        cts.Dispose();

        // Повторный Dispose не должен выбрасывать
        Assert.DoesNotThrow(cts.Dispose);
    }

    /// <summary>
    /// Тест: Reset после Dispose безопасен.
    /// </summary>
    [TestCase(TestName = "RCTS: Reset после Dispose безопасен"), Benchmark]
    public void ResetAfterDisposeIsSafe()
    {
        var cts = new ResettableCancellationTokenSource();
        cts.Dispose();

        // Reset после Dispose должен быть безопасным (новый CTS создаётся и тут же уничтожается)
        Assert.DoesNotThrow(cts.Reset);
    }

    #endregion
}
