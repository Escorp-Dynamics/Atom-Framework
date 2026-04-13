namespace Atom.Net.Browsing.WebDriver;

internal sealed class BridgeTabChannel(string sessionId, string tabId, string? windowId, DateTimeOffset registeredAtUtc)
{
    public string SessionId { get; } = sessionId;

    public string TabId { get; } = tabId;

    public string? WindowId { get; } = windowId;

    public DateTimeOffset RegisteredAtUtc { get; } = registeredAtUtc;

    public DateTimeOffset LastSeenAtUtc { get; set; } = registeredAtUtc;

    public bool IsRegistered { get; set; } = true;
}