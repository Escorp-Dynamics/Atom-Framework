using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgePendingRequestDescriptor(
    string MessageId,
    string SessionId,
    string TabId,
    BridgeCommand Command = BridgeCommand.GetTitle,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? DeadlineUtc = null,
    TaskCompletionSource<BridgeMessage>? CompletionSource = null);