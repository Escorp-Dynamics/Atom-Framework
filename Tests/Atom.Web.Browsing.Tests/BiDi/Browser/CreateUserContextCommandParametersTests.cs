namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Newtonsoft.Json.Linq;

[TestFixture]
public class CreateUserContextCommandParametersTests
{
    [Test]
    public void TestCommandName()
    {
        CreateUserContextCommandParameters properties = new();
        Assert.That(properties.MethodName, Is.EqualTo("browser.createUserContext"));
    }

    [Test]
    public void TestCanSerializeParameters()
    {
        CreateUserContextCommandParameters properties = new();
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.CreateUserContextCommandParameters);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Is.Empty);
    }
}
