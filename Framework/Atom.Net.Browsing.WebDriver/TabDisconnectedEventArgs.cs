namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Аргументы события отключения вкладки от моста.
/// </summary>
public sealed class TabDisconnectedEventArgs : EventArgs
{
    /// <summary>
    /// Идентификатор отключённой вкладки.
    /// </summary>
    public string TabId { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TabDisconnectedEventArgs"/>.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    internal TabDisconnectedEventArgs(string tabId) => TabId = tabId;
}
