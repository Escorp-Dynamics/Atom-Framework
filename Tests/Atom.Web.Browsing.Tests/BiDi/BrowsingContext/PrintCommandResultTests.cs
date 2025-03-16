namespace Atom.Web.Browsing.BiDi.BrowsingContext;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class PrintCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "data": "some print data"
                      }
                      """;
        PrintCommandResult? result = JsonSerializer.Deserialize<PrintCommandResult>(json, JsonContext.Default.PrintCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Data, Is.EqualTo("some print data"));
    }

    [Test]
    public void TestDeserializingWithMissingDataThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<PrintCommandResult>(json, JsonContext.Default.PrintCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDataTypeThrows()
    {
        string json = """
                      {
                        "data": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<PrintCommandResult>(json, JsonContext.Default.PrintCommandResult), Throws.InstanceOf<JsonException>());
    }
}
