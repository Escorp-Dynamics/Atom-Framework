namespace Atom.Buffers.Tests;

public class ObjectPoolTests(ILogger logger) : BenchmarkTests<ObjectPoolTests>(logger)
{
    private sealed class TestObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public ObjectPoolTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест аренды объекта"), Benchmark(Baseline = true)]
    public void RentTest()
    {
        var obj = ObjectPool<TestObject>.Shared.Rent();
        if (!IsBenchmarkEnabled) Assert.That(obj, Is.Not.Null);

        obj.Id = 5;
        obj.Name = "test";
        ObjectPool<TestObject>.Shared.Return(obj);

        obj = ObjectPool<TestObject>.Shared.Rent();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(obj, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(obj.Id, Is.EqualTo(5));
                Assert.That(obj.Name, Is.EqualTo("test"));
            });
        }

        ObjectPool<TestObject>.Shared.Return(obj);
    }

    [TestCase(TestName = "Тест аллокации объекта"), Benchmark(Baseline = true)]
    public void AllocTest()
    {
        {
            var obj = new TestObject();
            if (!IsBenchmarkEnabled) Assert.That(obj, Is.Not.Null);

            obj.Id = 5;
            obj.Name = "test";
        }

        {
            var obj = new TestObject();
            if (!IsBenchmarkEnabled) Assert.That(obj, Is.Not.Null);
        }
    }

    [TestCase(TestName = "Тест аренды объекта со сбросом свойств"), Benchmark]
    public void RentWithClearingTest()
    {
        var obj = ObjectPool<TestObject>.Shared.Rent();
        if (!IsBenchmarkEnabled) Assert.That(obj, Is.Not.Null);

        obj.Id = 5;
        obj.Name = "test";

        ObjectPool<TestObject>.Shared.Return(obj, x =>
        {
            x.Id = default;
            x.Name = string.Empty;
        });

        obj = ObjectPool<TestObject>.Shared.Rent();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(obj, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(obj.Id, Is.EqualTo(0));
                Assert.That(obj.Name, Is.EqualTo(string.Empty));
            });
        }

        ObjectPool<TestObject>.Shared.Return(obj);
    }


    [TestCase(TestName = "Тест аллокации объекта со сбросом свойств"), Benchmark]
    public void AllocWithClearingTest()
    {
        {
            var obj = new TestObject();
            if (!IsBenchmarkEnabled) Assert.That(obj, Is.Not.Null);

            obj.Id = 5;
            obj.Name = "test";

            obj.Id = default;
            obj.Name = string.Empty;
        }

        {
            var obj = new TestObject();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(obj, Is.Not.Null);

                Assert.Multiple(() =>
                {
                    Assert.That(obj.Id, Is.EqualTo(0));
                    Assert.That(obj.Name, Is.EqualTo(string.Empty));
                });
            }
        }
    }
}