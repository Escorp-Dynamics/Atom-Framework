using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Analytics;
using Atom.Web.Analytics.Tests;

namespace Atom.Web.Tests;

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
public partial class JsonTestContext : JsonSerializerContext;

public class CurrencyTests
{
    [Fact]
    public void ParseTest()
    {
        Assert.True(Currency.TryParse("RUB", out var currency));
        Assert.NotNull(currency);
        Assert.True(currency.IsoCode is "RUB");
    }

    [Fact]
    public void SerializeTest()
    {
        var currency = Currency.RUB;

        var form = new Dictionary<string, object?>
        {
            {"currency", currency}
        };

        var element = JsonSerializer.Serialize(form!, JsonTestsContext.Default.Form);
        Assert.True(element is "{\n  \"currency\": \"RUB\"\n}");

        var test1 = new Test1 { Currency = currency, };

        element = JsonSerializer.Serialize(test1, JsonTestContext.Default.Test1);
        Assert.True(element is "{\n  \"currency\": \"RUB\"\n}");
    }
}