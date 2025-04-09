namespace Atom.Web.Analytics.Tests;

public class ContinentTests(ILogger logger) : BenchmarkTests<ContinentTests>(logger)
{
    public ContinentTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест парсинга"), Benchmark]
    public void ParseTest()
    {
        var result = Continent.TryParse("an", out var continent);
        if (IsBenchmarkEnabled) return;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(continent, Is.Not.Null);
        });

        Assert.That(continent?.Code, Is.EqualTo("AN"));
    }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var continent = Continent.AN;
        var json = continent.Serialize();

        if (!IsBenchmarkEnabled) Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "\"AN\""));
    }
}