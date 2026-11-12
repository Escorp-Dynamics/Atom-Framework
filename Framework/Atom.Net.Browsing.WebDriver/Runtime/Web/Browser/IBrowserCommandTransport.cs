using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Транспорт управления браузером: выдаёт каналы команд вкладок и публикует события рантайма.
/// </summary>
/// <remarks>
/// Отделяет <see cref="WebBrowser"/> от конкретного способа управления: мостового расширения
/// (<see cref="BridgeBrowserCommandTransport"/>) или протокола отладки.
/// </remarks>
internal interface IBrowserCommandTransport : IAsyncDisposable
{
    /// <summary>Событие рантайма вкладки: идентификатор сессии и сообщение.</summary>
    event Action<string, BridgeMessage>? RuntimeEventReceived;

    event BridgeCallbackHandler? CallbackRequested;

    event BridgeRequestInterceptionHandler? RequestInterceptionRequested;

    event BridgeResponseInterceptionHandler? ResponseInterceptionRequested;

    /// <summary>
    /// Возвращает зарегистрированную вкладку сессии, удовлетворяющую условию, либо <see langword="null"/>,
    /// если сессия ещё не подключена или такой вкладки нет.
    /// </summary>
    ValueTask<BridgeTabChannelSnapshot?> TryGetRegisteredTabAsync(string sessionId, Func<BridgeTabChannelSnapshot, bool> predicate);

    /// <summary>
    /// Создаёт канал команд для вкладки, к которой привязывается страница.
    /// </summary>
    IPageCommandClient CreatePageCommands(WebPage page, string sessionId, string tabId);
}
