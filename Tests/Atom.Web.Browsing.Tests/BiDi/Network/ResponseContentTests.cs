namespace Atom.Web.Browsing.BiDi.Network;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class ResponseContentTests
{
    [Test]
    public void TestCanDeserializeResponseContent()
    {
        string json = """
                      {
                        "size": 300
                      }
                      """;
        ResponseContent? responseContent = JsonSerializer.Deserialize<ResponseContent>(json, JsonContext.Default.ResponseContent);
        Assert.That(responseContent, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(responseContent!.Size, Is.EqualTo(300));
        });
    }

    [Test]
    public void TestDeserializeWithMissingSizeThrows()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<ResponseContent>(json, JsonContext.Default.ResponseContent), Throws.InstanceOf<JsonException>().With.Message.Contains("was missing required properties including: 'size'"));
    }
}
