namespace Atom.Net.Browsing.WebDriver;

internal enum TabRemovalResultKind
{
    Removed,
    TabNotFound,
    TabOwnedByAnotherSession,
    SessionNotFound,
}

internal sealed record TabRemovalResult(
    TabRemovalResultKind Outcome,
    BridgeTabChannelSnapshot? Tab,
    int FailedPendingRequestCount);