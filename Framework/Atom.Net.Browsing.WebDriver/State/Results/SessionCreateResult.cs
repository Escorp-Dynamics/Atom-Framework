namespace Atom.Net.Browsing.WebDriver;

internal enum SessionCreateResultKind
{
    Created,
    DuplicateSessionId,
    InvalidDescriptor,
}

internal sealed record SessionCreateResult(
    SessionCreateResultKind Outcome,
    BridgeBrowserSessionSnapshot? Session);