using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Аргументы события, полученного от вкладки через мост.
/// </summary>
/// <param name="message">Сообщение события.</param>
public sealed class TabChannelEventArgs(BridgeMessage message) : EventArgs
{
    /// <summary>
    /// Сообщение, содержащее данные события.
    /// </summary>
    public BridgeMessage Message { get; } = message;
}
