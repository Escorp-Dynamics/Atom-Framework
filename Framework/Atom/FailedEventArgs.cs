namespace Atom;

/// <summary>
/// Представляет аргументы события неудачной процедуры.
/// </summary>
public class FailedEventArgs : MutableEventArgs
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

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FailedEventArgs"/>.
    /// </summary>
    public FailedEventArgs() : base() { }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        Exception = default;
        IsRetry = default;
        Timeout = default;
    }
}