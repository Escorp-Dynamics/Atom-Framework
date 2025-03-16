namespace Atom.Web.Browsing.BiDi.Protocol;

using System.Text.Json;
using Newtonsoft.Json.Linq;
using Atom.Web.Browsing.BiDi.TestUtilities;
using Atom.Web.Browsing.BiDi.JsonConverters.Tests;

[TestFixture]
public class CommandTests
{
    [Test]
    public void TestCanSerializeCommand()
    {
        string commandName = "module.command";
        Dictionary<string, object?> expectedCommandParameters = new()
        {
            { "parameterName", "parameterValue" },
        };
        Dictionary<string, object?> expected = new()
        {
            { "id", 1 },
            { "method", commandName },
            { "params", expectedCommandParameters },
            { "overflowParameterName", "overflowParameterValue" },
        };

        TestCommandParameters commandParams = new TestCommandParameters(commandName);
        commandParams.AdditionalData["overflowParameterName"] = "overflowParameterValue";

        Command command = new(1, commandParams, JsonTestContext.Default.TestCommandParameters, JsonContext.Default.Command);
        string json = JsonSerializer.Serialize(command, JsonContext.Default.Command);
        Dictionary<string, object?> dataValue = JObject.Parse(json).ToParsedDictionary();       
        Assert.That(dataValue, Is.EquivalentTo(expected));
    }

    [Test]
    public void TestCannotDeserializeCommand()
    {
        string json = """
                      {
                        "id": 1,
                        "method": "module.command",
                        "params": {
                          "paramName": "paramValue"
                        }
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<Command>(json, JsonContext.Default.Command), Throws.InstanceOf<NotImplementedException>());
    }
}