namespace Atom.Debug;

/// <summary>
/// Представляет аргументы события выполнения консольной команды.
/// </summary>
/// <param name="args">Аргументы команды.</param>
public class ExecuteCommandEventArgs(IEnumerable<string> args) : AsyncEventArgs
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