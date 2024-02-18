using Atom.Text;

namespace Atom.Debug;

public class ParseCommandEventArgs(string origin, IEnumerable<string> args) : ParseEventArgs(origin)
{
    public IEnumerable<string> Args { get; protected set; } = args;
}