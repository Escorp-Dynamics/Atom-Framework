namespace Atom.Net.Browsing.WebDriver;

internal sealed class BridgeBrowserSession(
    string sessionId,
    int protocolVersion,
    DateTimeOffset connectedAtUtc,
    string browserFamily,
    string extensionVersion,
    string? browserVersion,
    long connectionEpoch = 0)
{
    public string SessionId { get; } = sessionId;

    /// <summary>
    /// Поколение транспортного соединения, владеющего записью сессии. Удаление чужого
    /// поколения (например, от прежнего, вытесненного реконнектом соединения) игнорируется.
    /// </summary>
    public long ConnectionEpoch { get; } = connectionEpoch;

    public int ProtocolVersion { get; } = protocolVersion;

    public DateTimeOffset ConnectedAtUtc { get; } = connectedAtUtc;

    public DateTimeOffset LastSeenAtUtc { get; set; } = connectedAtUtc;

    public string BrowserFamily { get; } = browserFamily;

    public string ExtensionVersion { get; } = extensionVersion;

    public string? BrowserVersion { get; } = browserVersion;

    public bool IsConnected { get; set; } = true;

    public Dictionary<string, BridgeTabChannel> ChannelsByTabId { get; } = new(StringComparer.Ordinal);
}