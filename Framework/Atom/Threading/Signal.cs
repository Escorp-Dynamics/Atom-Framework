using System.Runtime.CompilerServices;

namespace Atom.Threading;

/// <summary>
/// Представляет сигнал.
/// </summary>
public class Signal<T>
{
    /// <summary>
    /// Происходит в момент отправки сигнала.
    /// </summary>
    public event MutableEventHandler<object, SignalEventArgs<T>>? Sended;

    /// <summary>
    /// Посылает сигнал.
    /// </summary>
    /// <param name="state">Состояние.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send(T? state) => Sended.On(this, args => args.State = state);

    /// <summary>
    /// Посылает сигнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send() => Send(default);
}

/// <summary>
/// Представляет сигнал.
/// </summary>
public class Signal : Signal<object>;