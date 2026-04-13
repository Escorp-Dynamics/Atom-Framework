namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeSessionDescriptor(
    string SessionId,
    int ProtocolVersion,
    string BrowserFamily,
    string ExtensionVersion,
    string? BrowserVersion = null,
    DateTimeOffset? ConnectedAtUtc = null);