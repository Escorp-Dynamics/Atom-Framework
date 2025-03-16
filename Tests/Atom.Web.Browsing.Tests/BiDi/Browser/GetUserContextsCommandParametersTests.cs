namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Newtonsoft.Json.Linq;

[TestFixture]
public class GetUserContextsCommandParametersTests
{
    [Test]
    public void TestCommandName()
    {
        GetUserContextsCommandParameters properties = new();
        Assert.That(properties.MethodName, Is.EqualTo("browser.getUserContexts"));
    }

    [Test]
    public void TestCanSerializeParameters()
    {
        GetUserContextsCommandParameters properties = new();
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.GetUserContextsCommandParameters);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Is.Empty);
    }
}
