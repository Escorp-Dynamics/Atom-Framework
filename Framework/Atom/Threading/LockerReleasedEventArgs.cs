namespace Atom.Threading;

/// <summary>
/// Представляет событие освобождения ожиданий блокировщика.
/// </summary>
public class LockerReleasedEventArgs : MutableEventArgs
{
    /// <summary>
    /// Количество освобождённых ожиданий блокировщика.
    /// </summary>
    public int ReleaseCount { get; protected internal set; }
}