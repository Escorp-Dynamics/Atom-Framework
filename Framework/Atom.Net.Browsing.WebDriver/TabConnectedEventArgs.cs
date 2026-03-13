namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Аргументы события подключения вкладки к мосту.
/// </summary>
public sealed class TabConnectedEventArgs : EventArgs
{
    /// <summary>
    /// Идентификатор подключённой вкладки.
    /// </summary>
    public string TabId { get; }

    /// <summary>
    /// Канал связи с вкладкой.
    /// </summary>
    internal TabChannel Channel { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TabConnectedEventArgs"/>.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    /// <param name="channel">Канал связи.</param>
    internal TabConnectedEventArgs(string tabId, TabChannel channel)
    {
        TabId = tabId;
        Channel = channel;
    }
}
