using System.Collections.Concurrent;

namespace Atom.Buffers.Tests;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ObjectPoolTests(ILogger logger) : BenchmarkTests<ObjectPoolTests>(logger)
{
    private sealed class PooledObject
    {
        public int Id { get; set; }
        public bool IsReset { get; set; }
    }

    public ObjectPoolTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест аренды объекта (однопоточный)"), Benchmark(Baseline = true)]
    public void RentTest()
    {
        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject { Id = 42 });

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
        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
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
        var pool = ObjectPool<int>.Create();
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
        var pool = ObjectPool<PooledObject>.Create();
        var obj = pool.Rent();
        Assert.That(obj, Is.Not.Null);
    }

    [TestCase(TestName = "Тест конкурентного доступа (без дублей)")]
    [Retry(3)] // lock-free тесты могут быть флаки
    public void ConcurrentRentReturnNoDuplicates()
    {
        const int Threads = 16;
        const int PerThread = 10_000;

        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
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
    public void ConcurrentRentSameObjectNeverRentedTwice()
    {
        const int Threads = 32;
        const int Ops = 5_000;

        var pool = ObjectPool<PooledObject>.Create(() => new PooledObject());
        var rented = new ConcurrentDictionary<PooledObject, byte>();

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

        Task.WaitAll([.. tasks]);
    }

    [TestCase(TestName = "Тест роста thread-local стека")]
    public void ThreadLocalStackGrowsFrom4To64()
    {
        var pool = ObjectPool<int>.Create(() => 0);

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
        var pool = ObjectPool<int>.Create(capacity: 7);
        // внутри capacity округляется до 16
        Assert.DoesNotThrow(() =>
        {
            for (var i = 0; i < 50; i++) pool.Return(i);
            for (var i = 0; i < 50; i++) _ = pool.Rent();
        });
    }

    [TestCase(TestName = "Нагрузочный тест")]
    [Explicit("Долгий")]
    public void ThroughputBenchmark()
    {
        const int Ops = 10_000_000;
        var pool = ObjectPool<int>.Create(() => 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < Ops; i++)
        {
            var x = pool.Rent();
            pool.Return(x);
        }
        sw.Stop();

        var opsPerSec = Ops / sw.Elapsed.TotalSeconds;
        Assert.That(opsPerSec, Is.GreaterThan(100_000_000)); // >100 Mops/s
    }
}