namespace Atom.Web.Browsing.BiDi.WebExtension;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class InstallCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "extension": "myExtensionId"
                      }
                      """;
        InstallCommandResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.InstallCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ExtensionId, Is.EqualTo("myExtensionId"));
    }

    [Test]
    public void TestDeserializingWithMissingExtensionThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.InstallCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidExtensionTypeThrows()
    {
        string json = """
                      {
                        "extension": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.InstallCommandResult), Throws.InstanceOf<JsonException>());
    }
}
