namespace Atom.Web.Browsing.BiDi.TestUtilities;

public class TestCommandSetEventArgs: BiDiEventArgs
{
    private readonly string commandName;
    private readonly Type resultType;

    public TestCommandSetEventArgs(string commandName, Type resultType)
    {
        this.commandName = commandName;
        this.resultType = resultType;
    }

    public string MethodName => this.commandName;

    public Type ResultType => this.resultType;
}
