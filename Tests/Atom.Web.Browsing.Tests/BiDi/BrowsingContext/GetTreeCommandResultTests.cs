namespace Atom.Web.Browsing.BiDi.BrowsingContext;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class GetTreeCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "contexts": [
                          {
                            "context": "myContextId",
                            "clientWindow": "myClientWindow",
                            "url": "http://example.com",
                            "originalOpener": "openerContext",
                            "userContext": "default",
                            "children": []
                          }
                        ]
                      }
                      """;
        GetTreeCommandResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.GetTreeCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContextTree.ToArray(), Has.Length.EqualTo(1));
    }

    [Test]
    public void TestCanDeserializeWithNoContexts()
    {
        string json = """
                      {
                        "contexts": []
                      }
                      """;
        GetTreeCommandResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.GetTreeCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContextTree, Is.Empty);
    }

    [Test]
    public void TestDeserializingWithMissingContextsThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.GetTreeCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidContextsTypeThrows()
    {
        string json = """
                      {
                        "contexts": "invalid"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.GetTreeCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidContextValueTypeThrows()
    {
        string json = """
                      {
                        "contexts": [ "invalid" ]
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.GetTreeCommandResult), Throws.InstanceOf<JsonException>());
    }
}
