namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Newtonsoft.Json.Linq;

[TestFixture]
public class CloseCommandParametersTests
{
    [Test]
    public void TestCommandName()
    {
        CloseCommandParameters properties = new();
        Assert.That(properties.MethodName, Is.EqualTo("browser.close"));
    }

    [Test]
    public void TestCanSerializeParameters()
    {
        CloseCommandParameters properties = new();
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.BrowserCloseCommandParameters);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Is.Empty);
    }
}
