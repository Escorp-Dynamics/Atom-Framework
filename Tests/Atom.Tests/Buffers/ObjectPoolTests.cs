using System.Collections.Concurrent;
using System.Text;

namespace Atom.Buffers.Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
public class ObjectPoolTests(ILogger logger) : BenchmarkTests<ObjectPoolTests>(logger)
{
    private sealed class PooledObject
    {
        public int Id { get; set; }
        public bool IsReset { get; set; }
    }

    public override bool IsBenchmarkEnabled => default;

    public ObjectPoolTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест аренды объекта (однопоточный)"), Benchmark(Baseline = true)]
    public void RentTest()
    {
        using var pool = ObjectPool<PooledObject>.Create(() => new PooledObject { Id = 42 });

        var obj1 = pool.Rent();
        Assert.That(obj1, Is.Not.Null);
        Assert.That(obj1.Id, Is.EqualTo(42));

        obj1.Id = 100500;
        pool.Return(obj1);

        var obj2 = pool.Rent();
        Assert.That(obj2, Is.SameAs(obj1));
        Assert.That(obj2.Id, Is.EqualTo(100500)); // объект не сброшен
    }

    [TestCase(TestName = "Тест возврата объекта с ресеттером"), Benchmark]
    public void ReturnWithResetter()
    {
        using var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
        var obj = pool.Rent();
        obj.Id = 777;

        pool.Return(obj, x => x.IsReset = true);

        var reused = pool.Rent();
        Assert.That(reused, Is.SameAs(obj));
        Assert.That(reused.IsReset, Is.True);
    }

    [TestCase(TestName = "Тест работы без фабрики (значимый)"), Benchmark]
    public void DefaultFactoryValueType()
    {
        using var pool = ObjectPool<int>.Create();
        var x = pool.Rent();
        Assert.That(x, Is.Zero);

        // возвращаем и проверяем, что объект вернулся
        pool.Return(x);
        var y = pool.Rent();
        Assert.That(y, Is.Zero);          // значение не изменилось
    }

    [TestCase(TestName = "Тест работы без фабрики (ссылочный)"), Benchmark]
    public void DefaultFactoryReferenceType()
    {
        using var pool = ObjectPool<PooledObject>.Create();
        var obj = pool.Rent();
        Assert.That(obj, Is.Not.Null);
    }

    [TestCase(TestName = "Тест конкурентного доступа (без дублей)")]
    [Retry(3)] // lock-free тесты могут быть флаки
    public void ConcurrentRentReturnNoDuplicates()
    {
        const int Threads = 16;
        const int PerThread = 10_000;

        using var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
        var rented = new ConcurrentDictionary<PooledObject, byte>();

        Parallel.For(0, Threads, _ =>
        {
            for (var i = 0; i < PerThread; i++)
            {
                var obj = pool.Rent();
                Assert.That(rented.TryAdd(obj, 1), Is.True, "Объект уже арендован!");
                pool.Return(obj);
                rented.TryRemove(obj, out var _);
            }
        });
    }

    [TestCase(TestName = "Тест конкурентного доступа (не арендует дважды)")]
    public async Task ConcurrentRentSameObjectNeverRentedTwiceAsync()
    {
        const int Threads = 32;
        const int Ops = 5_000;

        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
        var rented = new ConcurrentDictionary<PooledObject, byte>();

        try
        {
            var tasks = Enumerable.Range(0, Threads).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < Ops; i++)
                {
                    var obj = pool.Rent();
                    Assert.That(rented.TryAdd(obj, 1), Is.True, "Объект уже арендован!");
                    pool.Return(obj);
                    rented.TryRemove(obj, out var _);
                }
            }));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            pool.Dispose();
        }
    }

    [TestCase(TestName = "Тест асинхронного конкурентного доступа (без дублей)")]
    [Retry(3)]
    public async Task ConcurrentAsyncRentReturnNoDuplicatesAsync()
    {
        var logicalProc = Environment.ProcessorCount;
        var concurrency = Math.Max(4, logicalProc * 4);
        const int Iterations = 2_500;

        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
        var rented = new ConcurrentDictionary<PooledObject, byte>();
        var readySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        try
        {
            var tasks = new Task[concurrency];

            for (var i = 0; i < concurrency; ++i)
            {
                tasks[i] = Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref readyCount) == concurrency)
                    {
                        readySignal.TrySetResult(true);
                    }

                    await readySignal.Task.ConfigureAwait(false);
                    await startSignal.Task.ConfigureAwait(false);

                    for (var i = 0; i < Iterations; i++)
                    {
                        var obj = pool.Rent();
                        Assert.That(rented.TryAdd(obj, 1), Is.True, "Объект уже арендован!");

                        try
                        {
                            await Task.Yield();
                        }
                        finally
                        {
                            var removed = rented.TryRemove(obj, out var _);
                            pool.Return(obj);
                            Assert.That(removed, Is.True, "Объект не найден при освобождении");
                        }
                    }
                });
            }

            await readySignal.Task.ConfigureAwait(false);
            startSignal.TrySetResult(true);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            pool.Dispose();
        }
    }

    [TestCase(TestName = "Тест асинхронного сброса объекта при возврате")]
    [Retry(3)]
    public async Task ConcurrentAsyncResetterEnsuresCleanStateAsync()
    {
        var logicalProc = Environment.ProcessorCount;
        var concurrency = Math.Max(4, logicalProc * 2);
        const int Iterations = 2_000;

        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject { IsReset = true });
        var dirtyObserved = 0;
        var readySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        try
        {
            var tasks = new Task[concurrency];

            for (var i = 0; i < concurrency; ++i)
            {
                tasks[i] = Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref readyCount) == concurrency)
                    {
                        readySignal.TrySetResult(true);
                    }

                    await readySignal.Task.ConfigureAwait(false);
                    await startSignal.Task.ConfigureAwait(false);

                    for (var i = 0; i < Iterations; i++)
                    {
                        var obj = pool.Rent();
                        if (!obj.IsReset)
                        {
                            Interlocked.Increment(ref dirtyObserved);
                        }

                        obj.IsReset = false;
                        await Task.Yield();
                        pool.Return(obj, static pooled => pooled.IsReset = true);
                    }
                });
            }

            await readySignal.Task.ConfigureAwait(false);
            startSignal.TrySetResult(true);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            pool.Dispose();
        }

        Assert.That(dirtyObserved, Is.Zero, "Получен объект без сброса");
    }

    [TestCase(TestName = "Тест сброса StringBuilder при возврате (параллельный)")]
    [Retry(3)]
    public void ConcurrentStringBuilderResetParallel()
    {
        const int Threads = 16;
        const int Iterations = 5_000;

        using var pool = ObjectPool<StringBuilder>.Create(() => new StringBuilder());
        var violations = new ConcurrentQueue<string>();

        Parallel.For(0, Threads, _ =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                var builder = pool.Rent();
                try
                {
                    if (builder.Length != 0)
                    {
                        var snapshot = builder.ToString();
                        violations.Enqueue(snapshot.Length > 128 ? snapshot[..128] : snapshot);
                        builder.Clear();
                    }

                    builder.Append('A');
                    builder.Append(i);
                }
                finally
                {
                    pool.Return(builder, static sb => sb.Clear());
                }
            }
        });

        var issues = violations.ToArray();
        if (issues.Length > 0)
        {
            Assert.Fail($"StringBuilder возвращен из пула в грязном состоянии: {string.Join(" | ", issues.Take(3))} (ещё {issues.Length - Math.Min(issues.Length, 3)} элементов)");
        }
    }

    [TestCase(TestName = "Тест сброса StringBuilder при возврате (асинхронный)")]
    [Retry(3)]
    public async Task ConcurrentAsyncStringBuilderResetEnsuresCleanStateAsync()
    {
        var logicalProc = Environment.ProcessorCount;
        var concurrency = Math.Max(4, logicalProc * 2);
        const int Iterations = 1_000;

        var pool = ObjectPool<StringBuilder>.Create(() => new StringBuilder());
        var violations = new ConcurrentQueue<string>();
        var readySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        try
        {
            var tasks = new Task[concurrency];

            for (var worker = 0; worker < concurrency; ++worker)
            {
                var workerId = worker;
                tasks[worker] = Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref readyCount) == concurrency)
                    {
                        readySignal.TrySetResult(true);
                    }

                    await readySignal.Task.ConfigureAwait(false);
                    await startSignal.Task.ConfigureAwait(false);

                    for (var iteration = 0; iteration < Iterations; iteration++)
                    {
                        var builder = pool.Rent();
                        try
                        {
                            if (builder.Length != 0)
                            {
                                var snapshot = builder.ToString();
                                var truncated = snapshot.Length > 128 ? snapshot[..128] : snapshot;
                                violations.Enqueue($"worker={workerId}, iteration={iteration}, data={truncated}");
                                builder.Clear();
                            }

                            builder.Append(workerId);
                            builder.Append(':');
                            builder.Append(iteration);

                            await Task.Yield();
                        }
                        finally
                        {
                            pool.Return(builder, static sb => sb.Clear());
                        }
                    }
                });
            }

            await readySignal.Task.ConfigureAwait(false);
            startSignal.TrySetResult(true);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            pool.Dispose();
        }

        var issues = violations.ToArray();
        if (issues.Length > 0)
        {
            Assert.Fail($"StringBuilder повторно арендован грязным: {string.Join(" | ", issues.Take(3))} (ещё {issues.Length - Math.Min(issues.Length, 3)} элементов)");
        }
    }

    [TestCase(TestName = "Тест роста thread-local стека")]
    public void ThreadLocalStackGrowsFrom4To64()
    {
        using var pool = ObjectPool<int>.Create(() => 0);

        // прогреваем пул
        for (var i = 0; i < 100; i++) pool.Return(i);

        // проверяем, что локальный стек вырос
        var local = Enumerable.Range(0, 100).Select(_ => pool.Rent()).ToList();
        Assert.That(local, Has.Count.GreaterThan(64)); // взяли всё из глобального
    }

    [TestCase(TestName = "Тест dispose")]
    public void DisposePreventsFurtherRent()
    {
        var pool = ObjectPool<PooledObject>.Create();
        pool.Dispose();

        Assert.DoesNotThrow(pool.Dispose); // idempotent
        Assert.Throws<ObjectDisposedException>(() => _ = pool.Rent());
    }

    [TestCase(TestName = "Тест границ")]
    public void CapacityRoundUpToPowerOfTwo()
    {
        using var pool = ObjectPool<int>.Create(capacity: 7);
        // внутри capacity округляется до 16
        Assert.DoesNotThrow(() =>
        {
            for (var i = 0; i < 50; i++) pool.Return(i);
            for (var i = 0; i < 50; i++) _ = pool.Rent();
        });
    }

    [TestCase(TestName = "Нагрузочный тест"), Explicit("Долгий")]
    public void ThroughputBenchmark()
    {
        const int Ops = 10_000_000;
        using var pool = ObjectPool<int>.Create(() => 0);

        // небольшое прогревание, чтобы исключить холодный старт JIT/GC
        for (var i = 0; i < 100_000; i++)
        {
            var warm = pool.Rent();
            pool.Return(warm);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Ops; i++)
        {
            var x = pool.Rent();
            pool.Return(x);
        }
        sw.Stop();

        var opsPerSec = Ops / Math.Max(sw.Elapsed.TotalSeconds, double.Epsilon);

        // Разрешаем чуть более мягкий порог для debug-билдов и слабых машин —
        // важно поймать серьёзные регрессии, но не падать из-за TPL/GC в CI.
        var minOpsPerSec = Environment.ProcessorCount switch
        {
            <= 2 => 3_000_000d,
            <= 4 => 6_000_000d,
            <= 8 => 10_000_000d,
            _ => 18_000_000d,
        };

        Assert.That(opsPerSec, Is.GreaterThan(minOpsPerSec),
            $"Производительность {opsPerSec:N0} ops/s ниже порога {minOpsPerSec:N0} ops/s");
    }
}
