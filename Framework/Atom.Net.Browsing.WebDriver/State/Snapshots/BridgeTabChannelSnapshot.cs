namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeTabChannelSnapshot(
    string SessionId,
    string TabId,
    string? WindowId,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastSeenAtUtc,
    bool IsRegistered);