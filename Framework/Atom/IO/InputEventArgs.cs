namespace Atom.Debug;

/// <summary>
/// Представляет аргументы события ввода консоли.
/// </summary>
/// <param name="command">Введенная строка команды консоли.</param>
public class InputEventArgs(string command) : MutableEventArgs
{
    /// <summary>
    /// Введенная строка команды консоли.
    /// </summary>
    public string Command { get; protected set; } = command;
}