using System.Buffers;
using System.Collections.Concurrent;

namespace Atom.Collections.Tests;

public class SparseArrayTests(ILogger logger) : BenchmarkTests<SparseArrayTests>(logger)
{
    public SparseArrayTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест перебора через foreach"), Benchmark(Description = "foreach", Baseline = true)]
    public void ForEachTest()
    {
        var array = new SparseArray<int>(ushort.MaxValue + 1)
        {
            [100] = 2,
            [0] = 3
        };

        array.AddOrUpdate(67, -22);

        var count = 0;

        foreach (var item in array)
        {
            if (item is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        if (!IsBenchmarkEnabled) Assert.That(count, Is.EqualTo(3));
        array.Release();
    }

    [TestCase(TestName = "Тест перебора через foreach (SpanPool)"), Benchmark(Description = "foreach (SpanPool)")]
    public void ForEachSpanPoolTest()
    {
        var span = ArrayPool<int>.Shared.Rent(ushort.MaxValue + 1);
        SparseArray<int> array = span;

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var count = 0;

        foreach (var item in array)
        {
            if (item is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();
        ArrayPool<int>.Shared.Return(span);

        if (!IsBenchmarkEnabled) Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "Тест перебора через for"), Benchmark(Description = "for")]
    public void ForTest()
    {
        var array = new SparseArray<int>(ushort.MaxValue + 1)
        {
            [100] = 2,
            [0] = 3
        };

        array.AddOrUpdate(67, -22);

        var indexes = array.GetIndexes();
        var count = 0;

        for (var i = 0; i < indexes.Length; ++i)
        {
            if (array[indexes[i]] is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();

        if (!IsBenchmarkEnabled) Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "Тест перебора через for (SpanPool)"), Benchmark(Description = "for (SpanPool)")]
    public void ForSpanPoolTest()
    {
        var span = ArrayPool<int>.Shared.Rent(ushort.MaxValue + 1);
        SparseArray<int> array = span;

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var indexes = array.GetIndexes();
        var count = 0;

        for (var i = 0; i < indexes.Length; ++i)
        {
            if (array[indexes[i]] is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();
        ArrayPool<int>.Shared.Return(span);

        if (!IsBenchmarkEnabled) Assert.That(count, Is.EqualTo(3));
    }

    #region Race Condition Tests

    /// <summary>
    /// Тест на race condition при параллельном AddOrUpdate на один и тот же индекс.
    /// Проверяет, что индекс добавляется только один раз.
    /// </summary>
    [TestCase(TestName = "Race Condition: параллельный AddOrUpdate на один индекс")]
    [Retry(3)]
    public void ConcurrentAddOrUpdateSameIndexAddsOnce()
    {
        const int ThreadCount = 50;
        const int TargetIndex = 42;

        var array = new SparseArray<int>(100);
        using var barrier = new Barrier(ThreadCount);

        try
        {
            var tasks = new Task[ThreadCount];
            for (var i = 0; i < ThreadCount; i++)
            {
                var value = i;
                tasks[i] = Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                    array.AddOrUpdate(TargetIndex, value);
                });
            }

            Task.WaitAll(tasks);

            // Индекс должен быть добавлен только один раз
            var indexes = array.GetIndexes();
            var targetCount = indexes.ToArray().Count(idx => idx == TargetIndex);

            Assert.That(targetCount, Is.EqualTo(1), "Индекс должен быть добавлен только один раз");
        }
        finally
        {
            array.Release();
        }
    }

    /// <summary>
    /// Тест на race condition при параллельном AddOrUpdate на разные индексы.
    /// Проверяет, что все индексы добавлены корректно.
    /// </summary>
    [TestCase(TestName = "Race Condition: параллельный AddOrUpdate на разные индексы")]
    [Retry(3)]
    public void ConcurrentAddOrUpdateDifferentIndexesAddsAll()
    {
        const int ThreadCount = 50;

        var array = new SparseArray<int>(ThreadCount * 2);
        using var barrier = new Barrier(ThreadCount);

        try
        {
            var tasks = new Task[ThreadCount];
            for (var i = 0; i < ThreadCount; i++)
            {
                var index = i;
                var value = index * 10;
                tasks[i] = Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                    array.AddOrUpdate(index, value);
                });
            }

            Task.WaitAll(tasks);

            // Все индексы должны быть добавлены
            var indexes = array.GetIndexes().ToArray();
            Assert.That(indexes, Has.Length.EqualTo(ThreadCount), $"Должно быть {ThreadCount} индексов");

            // Проверяем, что все значения корректны
            for (var i = 0; i < ThreadCount; i++)
            {
                Assert.That(array[i], Is.EqualTo(i * 10), $"Значение по индексу {i} должно быть {i * 10}");
            }
        }
        finally
        {
            array.Release();
        }
    }

    /// <summary>
    /// Тест на race condition при параллельном Add.
    /// Проверяет, что все значения добавлены корректно.
    /// </summary>
    [TestCase(TestName = "Race Condition: параллельный Add")]
    [Retry(3)]
    public void ConcurrentAddAddsAll()
    {
        const int ThreadCount = 50;
        const int AddsPerThread = 10;

        var array = new SparseArray<int>(ThreadCount * AddsPerThread * 2);
        using var barrier = new Barrier(ThreadCount);

        try
        {
            var tasks = new Task[ThreadCount];
            for (var t = 0; t < ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                    for (var i = 0; i < AddsPerThread; i++)
                    {
                        array.Add((threadId * AddsPerThread) + i);
                    }
                });
            }

            Task.WaitAll(tasks);

            // Все значения должны быть добавлены
            var indexes = array.GetIndexes();
            Assert.That(indexes.Length, Is.EqualTo(ThreadCount * AddsPerThread),
                $"Должно быть {ThreadCount * AddsPerThread} элементов");
        }
        finally
        {
            array.Release();
        }
    }

    /// <summary>
    /// Стресс-тест: смешанные операции AddOrUpdate и Add параллельно.
    /// </summary>
    [TestCase(TestName = "Stress: смешанные операции AddOrUpdate и Add")]
    [Retry(3)]
    public void StressMixedAddOrUpdateAndAdd()
    {
        const int ThreadCount = 30;
        const int OpsPerThread = 100;

        var array = new SparseArray<int>(ThreadCount * OpsPerThread * 2);
        var addOrUpdateCount = 0;
        var addCount = 0;

        try
        {
            var tasks = new Task[ThreadCount];
            for (var t = 0; t < ThreadCount; t++)
            {
                var threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var random = new Random(threadId * 12345);
                    for (var i = 0; i < OpsPerThread; i++)
                    {
                        if (random.Next(2) == 0)
                        {
                            var index = random.Next(array.Length / 2);
                            array.AddOrUpdate(index, (threadId * 1000) + i);
                            Interlocked.Increment(ref addOrUpdateCount);
                        }
                        else
                        {
                            array.Add((threadId * 1000) + i);
                            Interlocked.Increment(ref addCount);
                        }
                    }
                });
            }

            Task.WhenAll(tasks).Wait(TimeSpan.FromMinutes(2));

            // Массив должен быть в консистентном состоянии
            var indexes = array.GetIndexes();
            Assert.That(indexes.Length, Is.GreaterThan(0), "Должны быть добавлены элементы");

            // Проверяем, что GetEnumerator работает без исключений
            var count = 0;
            foreach (var _ in array) count++;

            Assert.That(count, Is.EqualTo(indexes.Length), "Итератор должен вернуть все элементы");
        }
        finally
        {
            array.Release();
        }

        Logger.WriteLineInfo($"AddOrUpdate: {addOrUpdateCount}, Add: {addCount}");
    }

    /// <summary>
    /// Тест на корректное поведение после Release.
    /// </summary>
    [TestCase(TestName = "После Release операции бросают исключение")]
    public void AfterReleaseOperationsThrow()
    {
        var array = new SparseArray<int>(10);
        array.AddOrUpdate(0, 42);
        array.Release();

        Assert.That(array.IsReleased, Is.True);
        Assert.Throws<InvalidOperationException>(() => array.AddOrUpdate(0, 1));
        Assert.Throws<InvalidOperationException>(() => array.Add(1));
        Assert.Throws<InvalidOperationException>(() => _ = array[0]);
    }

    /// <summary>
    /// Тест на Reset в многопоточном окружении.
    /// </summary>
    [TestCase(TestName = "Race Condition: Reset во время операций")]
    [Retry(3)]
    public void ConcurrentResetDuringOperations()
    {
        const int WriterThreads = 20;
        const int OpsPerWriter = 500;
        const int ResetCount = 50;

        var array = new SparseArray<int>(WriterThreads * OpsPerWriter);
        var errors = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var writerTasks = new Task[WriterThreads];
            for (var t = 0; t < WriterThreads; t++)
            {
                var threadId = t;
                writerTasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < OpsPerWriter && !cts.Token.IsCancellationRequested; i++)
                        {
                            try
                            {
                                array.AddOrUpdate(threadId, i);
                            }
                            catch (InvalidOperationException)
                            {
                                // Может произойти если массив переполнен после Reset
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
            }

            var resetTask = Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < ResetCount && !cts.Token.IsCancellationRequested; i++)
                    {
                        Thread.Sleep(10);
                        array.Reset();
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            Task.WaitAll([.. writerTasks, resetTask]);

            Assert.That(errors, Is.Empty, $"Были ошибки: {string.Join(", ", errors.Select(e => e.Message))}");
        }
        finally
        {
            array.Release();
        }
    }

    #endregion
}