namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeHandshakeValidationResult(
    BridgeHandshakeValidationOutcome Outcome,
    string? CorrelationId,
    BridgeHandshakeClientPayload? ClientPayload,
    BridgeHandshakeAcceptPayload? AcceptPayload,
    string? RejectCode,
    BridgeHandshakeRejectPayload? RejectPayload)
{
    public bool IsAccepted => Outcome is BridgeHandshakeValidationOutcome.Accepted;

    public bool IsRejected => Outcome is BridgeHandshakeValidationOutcome.Rejected;
}