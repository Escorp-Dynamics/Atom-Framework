namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeHandshakeAcceptPayload(
    string SessionId,
    int NegotiatedProtocolVersion,
    int RequestTimeoutMs,
    int PingIntervalMs,
    int MaxMessageSize,
    long? ServerTimeUnixMs = null);