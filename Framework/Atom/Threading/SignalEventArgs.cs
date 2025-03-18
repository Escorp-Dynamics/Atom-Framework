namespace Atom.Threading;

/// <summary>
/// Представляет аргументы событий для <see cref="Signal{T}"/>.
/// </summary>
/// <typeparam name="T">Тип состояния.</typeparam>
public class SignalEventArgs<T> : MutableEventArgs
{
    /// <summary>
    /// Состояние.
    /// </summary>
    public T? State { get; internal set; }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        State = default;
    }
}

/// <summary>
/// Представляет аргументы событий для <see cref="Signal"/>.
/// </summary>
public class SignalEventArgs : SignalEventArgs<object>;