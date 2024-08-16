namespace Atom.Web.Browsers.NativeMessaging.Signals;

/// <summary>
/// Представляет аргументы события получения сигнала с клиента.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр класса <see cref="SignalReceivedAsyncEventArgs"/>.
/// </remarks>
/// <param name="signal">Полученный сигнал.</param>
public class SignalReceivedAsyncEventArgs(Signal signal) : AsyncEventArgs
{
    /// <summary>
    /// Полученный сигнал.
    /// </summary>
    public Signal Signal { get; set; } = signal;
}