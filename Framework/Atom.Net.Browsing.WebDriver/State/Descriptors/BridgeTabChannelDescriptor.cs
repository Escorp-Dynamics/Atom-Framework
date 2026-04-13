namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeTabChannelDescriptor(
    string SessionId,
    string TabId,
    string? WindowId = null,
    DateTimeOffset? RegisteredAtUtc = null);