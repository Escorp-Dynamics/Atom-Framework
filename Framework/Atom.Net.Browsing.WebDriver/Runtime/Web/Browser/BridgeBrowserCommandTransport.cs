using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Транспорт управления через мостовое расширение поверх <see cref="BridgeServer"/>.
/// </summary>
internal sealed class BridgeBrowserCommandTransport(BridgeServer server) : IBrowserCommandTransport
{
    internal BridgeServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    public event Action<string, BridgeMessage>? RuntimeEventReceived
    {
        add => Server.RuntimeEventReceived += value;
        remove => Server.RuntimeEventReceived -= value;
    }

    public event BridgeCallbackHandler? CallbackRequested
    {
        add => Server.CallbackRequested += value;
        remove => Server.CallbackRequested -= value;
    }

    public event BridgeRequestInterceptionHandler? RequestInterceptionRequested
    {
        add => Server.RequestInterceptionRequested += value;
        remove => Server.RequestInterceptionRequested -= value;
    }

    public event BridgeResponseInterceptionHandler? ResponseInterceptionRequested
    {
        add => Server.ResponseInterceptionRequested += value;
        remove => Server.ResponseInterceptionRequested -= value;
    }

    public async ValueTask<BridgeTabChannelSnapshot?> TryGetRegisteredTabAsync(string sessionId, Func<BridgeTabChannelSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var session = await Server.CreateSessionSnapshotAsync(sessionId).ConfigureAwait(false);
        if (session is not { IsConnected: true })
            return null;

        var tabs = await Server.GetTabsForSessionAsync(sessionId).ConfigureAwait(false);
        return tabs.FirstOrDefault(tab => tab.IsRegistered && predicate(tab));
    }

    public IPageCommandClient CreatePageCommands(WebPage page, string sessionId, string tabId)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.CreateBridgePageCommands(sessionId, tabId, Server.Commands);
    }

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
