namespace Atom.Web.Browsing.BiDi.Script;

using System.Text.Json;

[TestFixture]
public class RealmDestroyedEventArgsTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "realm": "myRealmId"
                      }
                      """;
        RealmDestroyedEventArgs? eventArgs = JsonSerializer.Deserialize(json, JsonContext.Default.RealmDestroyedEventArgs);
        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(eventArgs!.RealmId, Is.EqualTo("myRealmId"));
    }

    [Test]
    public void TestDeserializeWithMissingRealmValueThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RealmDestroyedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializeWithInvalidRealmValueThrows()
    {
        string json = """
                      {
                        "realm": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RealmDestroyedEventArgs), Throws.InstanceOf<JsonException>());
    }
}
