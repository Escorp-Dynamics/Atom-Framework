using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgePendingRequestSnapshot(
    string MessageId,
    string SessionId,
    string TabId,
    BridgeCommand Command,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeadlineUtc,
    bool IsCompleted);