namespace Atom.Debug;

/// <summary>
/// Аргументы события журнала.
/// </summary>
/// <param name="info">Данные записи журнала.</param>
public class LogEventArgs(ILogInfo info) : AsyncEventArgs
{
    /// <summary>
    /// Данные записи журнала.
    /// </summary>
    public ILogInfo Info { get; protected set; } = info;
}