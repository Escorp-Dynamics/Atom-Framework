namespace Atom.Net.Browsing.WebDriver;

internal enum PendingRequestAddResultKind
{
    Added,
    DuplicateMessageId,
    SessionNotFound,
    TabNotFound,
    InvalidDescriptor,
}

internal sealed record PendingRequestAddResult(
    PendingRequestAddResultKind Outcome,
    BridgePendingRequestSnapshot? Request);