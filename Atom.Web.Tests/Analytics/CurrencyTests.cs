using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Analytics.Tests;

public class Test1
{
    [JsonConverter(typeof(CurrencyJsonConverter<ushort>))]
    public Currency? Currency { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(Test1))]
public partial class JsonCurrencyTestContext : JsonSerializerContext;

[TestFixture]
public class CurrencyTests
{
    [Test]
    public void ParseTest()
    {
        var result = Currency.TryParse("RUB", out var currency);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(currency, Is.Not.Null);
        });

        Assert.That(currency.IsoCode, Is.EqualTo("RUB"));
    }

    [Test]
    public void SerializeTest()
    {
        var currency = Currency.RUB;

        var form = new Dictionary<string, object?>
        {
            {"currency", currency}
        };

        var element = JsonSerializer.Serialize(form!, JsonTestsContext.Default.Form);
        Assert.That(element, Is.EqualTo("{\n  \"currency\": \"RUB\"\n}"));

        var test1 = new Test1 { Currency = currency, };

        element = JsonSerializer.Serialize(test1, JsonCurrencyTestContext.Default.Test1);
        Assert.That(element, Is.EqualTo("{\n  \"currency\": \"RUB\"\n}"));
    }
}