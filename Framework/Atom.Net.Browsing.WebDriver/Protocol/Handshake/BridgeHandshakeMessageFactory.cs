using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal static class BridgeHandshakeMessageFactory
{
    public static BridgeMessage CreateAcceptMessage(BridgeHandshakeValidationResult validation)
        => new()
        {
            Id = validation.CorrelationId!,
            Type = BridgeMessageType.Handshake,
            Status = BridgeStatus.Ok,
            Payload = JsonSerializer.SerializeToElement(validation.AcceptPayload!, BridgeJsonContext.Default.BridgeHandshakeAcceptPayload),
        };

    public static BridgeMessage? CreateRejectMessage(BridgeHandshakeValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(validation.CorrelationId))
            return null;

        JsonElement? payload = null;
        if (validation.RejectPayload is not null)
        {
            payload = JsonSerializer.SerializeToElement(
                validation.RejectPayload,
                BridgeJsonContext.Default.BridgeHandshakeRejectPayload);
        }

        return new BridgeMessage
        {
            Id = validation.CorrelationId,
            Type = BridgeMessageType.Handshake,
            Status = BridgeStatus.Error,
            Error = validation.RejectCode,
            Payload = payload,
        };
    }
}