namespace Atom.Web.Browsing.BiDi.BrowsingContext;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class CreateCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "context": "myContextId"
                      }
                      """;
        CreateCommandResult? result = JsonSerializer.Deserialize<CreateCommandResult>(json, JsonContext.Default.CreateCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.BrowsingContextId, Is.EqualTo("myContextId"));
    }

    [Test]
    public void TestDeserializingWithMissingContextThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<CreateCommandResult>(json, JsonContext.Default.CreateCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidContextTypeThrows()
    {
        string json = """
                      {
                        "context": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<CreateCommandResult>(json, JsonContext.Default.CreateCommandResult), Throws.InstanceOf<JsonException>());
    }
}
