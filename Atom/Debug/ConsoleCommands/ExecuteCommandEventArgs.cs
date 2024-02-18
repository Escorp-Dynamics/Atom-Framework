namespace Atom.Debug;

public class ExecuteCommandEventArgs(IEnumerable<string> args) : AsyncEventArgs
{
    public IEnumerable<string> Args { get; protected set; } = args;

    public bool IsSuccess { get; set; }
}