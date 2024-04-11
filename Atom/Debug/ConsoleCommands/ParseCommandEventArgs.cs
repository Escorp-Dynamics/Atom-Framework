using Atom.Text;

namespace Atom.Debug;

/// <summary>
/// Представляет аргументы события парсинга команды консоли.
/// </summary>
/// <param name="origin">Исходная строка команды консоли.</param>
/// <param name="args">Аргументы команды.</param>
public class ParseCommandEventArgs(string origin, IEnumerable<string> args) : ParseEventArgs(origin)
{
    /// <summary>
    /// Аргументы команды.
    /// </summary>
    public IEnumerable<string> Args { get; protected set; } = args;
}