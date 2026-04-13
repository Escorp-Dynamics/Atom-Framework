namespace Atom.Net.Browsing.WebDriver;

internal enum TabRegistrationResultKind
{
    Registered,
    SessionNotFound,
    DuplicateTabId,
    AlreadyOwnedBySession,
    InvalidDescriptor,
}

internal sealed record TabRegistrationResult(
    TabRegistrationResultKind Outcome,
    BridgeTabChannelSnapshot? Tab);