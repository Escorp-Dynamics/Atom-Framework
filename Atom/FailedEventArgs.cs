namespace Atom;

/// <summary>
/// Представляет аргументы события неудачной процедуры.
/// </summary>
public class FailedEventArgs : AsyncEventArgs
{
    /// <summary>
    /// Исключение, возникшее в процессе выполнения процедуры.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Определяет, поддерживается ли возможность повторить выполнение процедуры после обработчика события.
    /// </summary>
    public bool IsRetry { get; set; }

    /// <summary>
    /// Время ожидания после выполнения обработчика.
    /// </summary>
    public TimeSpan Timeout { get; set; }
}