namespace Atom.Net.Browsing.WebDriver;

internal enum PendingRequestCompletionResultKind
{
    Completed,
    RequestNotFound,
    AlreadyCompleted,
}

internal sealed record PendingRequestCompletionResult(
    PendingRequestCompletionResultKind Outcome,
    BridgePendingRequestSnapshot? Request);