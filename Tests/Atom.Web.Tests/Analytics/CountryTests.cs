namespace Atom.Web.Analytics.Tests;

public class CountryTests(ILogger logger) : BenchmarkTests<CountryTests>(logger)
{
    public CountryTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест парсинга"), Benchmark]
    public void ParseTest()
    {
        var result = Country.TryParse("RU", out var country);
        if (IsBenchmarkEnabled) return;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(country, Is.Not.Null);
        });

        Assert.That(country?.IsoCode, Is.EqualTo("RUS"));
    }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var country = Country.RUS;
        var json = country.Serialize();

        if (!IsBenchmarkEnabled) Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "\"RUS\""));
    }
}