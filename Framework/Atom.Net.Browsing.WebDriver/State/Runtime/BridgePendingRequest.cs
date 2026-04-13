using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

internal sealed class BridgePendingRequest(
    string messageId,
    string sessionId,
    string tabId,
    BridgeCommand command,
    DateTimeOffset createdAtUtc,
    DateTimeOffset? deadlineUtc,
    TaskCompletionSource<BridgeMessage>? completionSource)
{
    public string MessageId { get; } = messageId;

    public string SessionId { get; } = sessionId;

    public string TabId { get; } = tabId;

    public BridgeCommand Command { get; } = command;

    public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;

    public DateTimeOffset? DeadlineUtc { get; } = deadlineUtc;

    public TaskCompletionSource<BridgeMessage> CompletionSource { get; } = completionSource
        ?? new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsCompleted { get; set; }
}