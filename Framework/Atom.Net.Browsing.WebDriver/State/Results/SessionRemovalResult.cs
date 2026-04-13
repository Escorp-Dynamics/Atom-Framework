namespace Atom.Net.Browsing.WebDriver;

internal enum SessionRemovalResultKind
{
    Removed,
    SessionNotFound,
}

internal sealed record SessionRemovalResult(
    SessionRemovalResultKind Outcome,
    BridgeBrowserSessionSnapshot? Session,
    int RemovedTabCount,
    int FailedPendingRequestCount);