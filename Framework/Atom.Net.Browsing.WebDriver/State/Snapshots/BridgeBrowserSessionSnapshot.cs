namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeBrowserSessionSnapshot(
    string SessionId,
    int ProtocolVersion,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string BrowserFamily,
    string ExtensionVersion,
    string? BrowserVersion,
    bool IsConnected,
    BridgeTabChannelSnapshot[] Tabs);