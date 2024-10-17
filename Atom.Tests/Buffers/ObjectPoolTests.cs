using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Buffers.Tests;

public class ObjectPoolTests(ILogger logger) : BenchmarkTest<ObjectPoolTests>(logger)
{
    private sealed class TestObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public override bool IsBenchmarkDisabled => true;

    public ObjectPoolTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест аренды объекта"), Benchmark(Baseline = true)]
    public void RentTest()
    {
        var obj = ObjectPool<TestObject>.Shared.Rent();
        if (IsTest) Assert.That(obj, Is.Not.Null);

        obj.Id = 5;
        obj.Name = "test";
        ObjectPool<TestObject>.Shared.Return(obj);

        obj = ObjectPool<TestObject>.Shared.Rent();

        if (IsTest)
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

    [TestCase(TestName = "Тест аренды объекта со сбросом свойств"), Benchmark]
    public void RentWithClearingTest()
    {
        var obj = ObjectPool<TestObject>.Shared.Rent();
        if (IsTest) Assert.That(obj, Is.Not.Null);

        obj.Id = 5;
        obj.Name = "test";

        ObjectPool<TestObject>.Shared.Return(obj, x =>
        {
            x.Id = default;
            x.Name = string.Empty;
        });

        obj = ObjectPool<TestObject>.Shared.Rent();

        if (IsTest)
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
}