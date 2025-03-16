namespace Atom.Web.Browsing.BiDi.BrowsingContext;

using System.Text.Json;
using Newtonsoft.Json.Linq;
using Atom.Web.Browsing.BiDi.Script;

[TestFixture]
public class LocateNodesCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "nodes": [
                          {
                            "type": "node", 
                            "sharedId": "mySharedId",
                            "value": {
                              "nodeType": 1,
                              "nodeValue": "",
                              "childNodeCount": 0
                            }
                          }
                        ]
                      }
                      """;
        LocateNodesCommandResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.LocateNodesCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Nodes.ToArray(), Has.Length.EqualTo(1));
        Assert.That(result!.Nodes.First().SharedId, Is.EqualTo("mySharedId"));
    }

    [Test]
    public void TestCanDeserializeWithEmptyResult()
    {
        string json = """
                      {
                        "nodes": []
                      }
                      """;
        LocateNodesCommandResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.LocateNodesCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Nodes.ToArray(), Has.Length.EqualTo(0));
    }

    [Test]
    public void TestDeserializingWithMissingDataThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.LocateNodesCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDataTypeThrows()
    {
        string json = """
                      {
                        ""nodes"": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.LocateNodesCommandResult), Throws.InstanceOf<JsonException>());
    }
}
