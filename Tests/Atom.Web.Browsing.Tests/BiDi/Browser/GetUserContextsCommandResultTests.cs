namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class GetUserContextsCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "userContexts": [
                          {
                            "userContext": "default"
                          },
                          {
                            "userContext": "myUserContext"
                          }
                        ]
                      }
                      """;
        GetUserContextsCommandResult? result = JsonSerializer.Deserialize<GetUserContextsCommandResult>(json, JsonContext.Default.GetUserContextsCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.UserContexts.ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.UserContexts.First().UserContextId, Is.EqualTo("default"));
            Assert.That(result.UserContexts.Last().UserContextId, Is.EqualTo("myUserContext"));
        });

    }

    [Test]
    public void TestDeserializingWithMissingDataThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<GetUserContextsCommandResult>(json, JsonContext.Default.GetUserContextsCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDataTypeThrows()
    {
        string json = """
                      {
                        "userContexts": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<GetUserContextsCommandResult>(json, JsonContext.Default.GetUserContextsCommandResult), Throws.InstanceOf<JsonException>());
    }
}
