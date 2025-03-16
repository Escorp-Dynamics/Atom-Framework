namespace Atom.Web.Browsing.BiDi.BrowsingContext;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class NavigationResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "url": "http://example.com"
                      }
                      """;
        NavigationResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.NavigationResult);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Url, Is.EqualTo(new Uri("http://example.com")));
            Assert.That(result.NavigationId, Is.Null);
        });
    }

    [Test]
    public void TestCanDeserializeWithNavigationId()
    {
        string json = """
                      {
                        "url": "http://example.com",
                        "navigation": "myNavigationId"
                      }
                      """;
        NavigationResult? result = JsonSerializer.Deserialize(json, JsonContext.Default.NavigationResult);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Url, Is.EqualTo(new Uri("http://example.com")));
            Assert.That(result.NavigationId, Is.EqualTo("myNavigationId"));
        });
    }

    [Test]
    public void TestDeserializingWithMissingUrlThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.NavigationResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidUrlTypeThrows()
    {
        string json = """
                      {
                        "url": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.NavigationResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidNavigationIdTypeThrows()
    {
        string json = """
                      {
                        "url": "http://example.co",
                        "navigation": {}
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.NavigationResult), Throws.InstanceOf<JsonException>());
    }
}
