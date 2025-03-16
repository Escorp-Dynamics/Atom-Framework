using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Web.Analytics.Tests;

public class CountryTests(ILogger logger) : BenchmarkTest<CountryTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public CountryTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест парсинга"), Benchmark]
    public void ParseTest()
    {
        var result = Country.TryParse("RU", out var country);
        if (!IsTest) return;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(country, Is.Not.Null);
        });

        Assert.That(country.IsoCode, Is.EqualTo("RUS"));
    }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var country = Country.RUS;
        var json = country.Serialize();

        if (IsTest) Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "\"RUS\""));
    }
}