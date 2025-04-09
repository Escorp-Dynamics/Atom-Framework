namespace Atom.Tests;

[TestFixture]
public class ExtensionsTests(ILogger logger) : BenchmarkTests<ExtensionsTests>(logger)
{
    [TestCase(TestName = "Тест получения дружественных имён типов"), Benchmark(Baseline = true)]
    public void GetFriendlyNameTest()
    {
        var type = typeof(int).GetFriendlyName();
        Assert.That(type, Is.EqualTo("int"));

        type = typeof(int).GetFriendlyName(default);
        Assert.That(type, Is.EqualTo("int"));

        type = typeof(int?).GetFriendlyName(default);
        Assert.That(type, Is.EqualTo("int?"));

        type = typeof(int?).GetFriendlyName();
        Assert.That(type, Is.EqualTo("int?"));
    }
}