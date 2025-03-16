namespace Atom.Web.Browsing.BiDi.TestUtilities;

using System.Text.Json.Serialization;

public class TestCommandParameters(string commandName, string parameterValue = "parameterValue") : CommandParameters<TestCommandResult>
{
    private readonly string commandName = commandName;

    [JsonIgnore]
    public override string MethodName => commandName;

    public string ParameterName { get; set; } = parameterValue;
}