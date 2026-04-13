namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeHandshakeRejectPayload(
    bool Retryable = false,
    int? SupportedProtocolVersion = null);