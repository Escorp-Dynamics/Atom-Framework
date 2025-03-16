using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Web.Analytics.Tests;

public class CurrencyTests(ILogger logger) : BenchmarkTest<CurrencyTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public CurrencyTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест парсинга"), Benchmark]
    public void ParseTest()
    {
        var result = Currency.TryParse("RUB", out var currency);
        if (!IsTest) return;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(currency, Is.Not.Null);
        });

        Assert.That(currency.IsoCode, Is.EqualTo("RUB"));
    }

    [TestCase(TestName = "Тест сериализации"), Benchmark]
    public void SerializeTest()
    {
        var currency = Currency.RUB;
        var json = currency.Serialize();

        if (IsTest) Assert.That(json, Is.EqualTo(/*lang=json,strict*/ "\"RUB\""));
    }
}