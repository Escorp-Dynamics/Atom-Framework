namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class CreateUserContextCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "userContext": "myUserContext"
                      }
                      """;
        CreateUserContextCommandResult? result = JsonSerializer.Deserialize<CreateUserContextCommandResult>(json, JsonContext.Default.CreateUserContextCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserContextId, Is.EqualTo("myUserContext"));
    }

    [Test]
    public void TestDeserializingWithMissingDataThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<CreateUserContextCommandResult>(json, JsonContext.Default.CreateUserContextCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDataTypeThrows()
    {
        string json = """
                      {
                        "userContext": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<CreateUserContextCommandResult>(json, JsonContext.Default.CreateUserContextCommandResult), Throws.InstanceOf<JsonException>());
    }
}
