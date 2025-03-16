using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Web.Analytics.Tests;

public class ContinentTests(ILogger logger) : BenchmarkTest<ContinentTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public ContinentTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест парсинга"), Benchmark]
    public void ParseTest()
    {
        var result = Continent.TryParse("an", out var continent);
        if (!IsTest) return;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(continent, Is.Not.Null);
        });

        Assert.That(continent.Code, Is.EqualTo("AN"));
    }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var continent = Continent.AN;
        var json = continent.Serialize();

        if (IsTest) Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "\"AN\""));
    }
}