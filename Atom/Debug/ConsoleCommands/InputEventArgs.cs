namespace Atom.Debug;

public class InputEventArgs(string command) : AsyncEventArgs
{
    public string Command { get; protected set; } = command;
}