namespace Atom.Net.Browsing.WebDriver;

internal sealed record BridgeServerHealthSnapshot(
    int SessionCount,
    int TabCount,
    int PendingRequestCount,
    long CompletedRequestCount,
    long FailedRequestCount);