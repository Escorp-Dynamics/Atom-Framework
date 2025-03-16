namespace Atom.Web.Browsing.BiDi.Network;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class AddInterceptCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "intercept": "myInterceptId"
                      }
                      """;
        AddInterceptCommandResult? result = JsonSerializer.Deserialize<AddInterceptCommandResult>(json, JsonContext.Default.AddInterceptCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.InterceptId, Is.EqualTo("myInterceptId"));
    }

    [Test]
    public void TestDeserializingWithMissingDataThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<AddInterceptCommandResult>(json, JsonContext.Default.AddInterceptCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDataTypeThrows()
    {
        string json = """
                      {
                        "intercept": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<AddInterceptCommandResult>(json, JsonContext.Default.AddInterceptCommandResult), Throws.InstanceOf<JsonException>());
    }
}
