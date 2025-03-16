namespace Atom.Debug;

/// <summary>
/// Представляет аргументы события выполнения команды консоли.
/// </summary>
/// <param name="args">Аргументы команды.</param>
public class ExecuteCommandEventArgs(IEnumerable<string> args) : MutableEventArgs
{
    /// <summary>
    /// Аргументы команды.
    /// </summary>
    public IEnumerable<string> Args { get; protected set; } = args;

    /// <summary>
    /// Указывает, было ли выполнение завершено успешно.
    /// </summary>
    public bool IsSuccess { get; set; }
}