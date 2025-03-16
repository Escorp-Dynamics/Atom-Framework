namespace Atom.Threading;

/// <summary>
/// Представляет аргументы события секвенсора задач.
/// </summary>
public class SequenceEventArgs : MutableEventArgs
{
    /// <summary>
    /// Текущая задача.
    /// </summary>
    public Func<ValueTask>? Task { get; internal set; }

    /// <summary>
    /// Режим выполнения задачи.
    /// </summary>
    public SequenceMode Mode { get; internal set; }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();

        Task = default;
        Mode = default;
    }
}