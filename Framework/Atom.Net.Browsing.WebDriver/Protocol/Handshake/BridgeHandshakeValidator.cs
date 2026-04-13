using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal static class BridgeHandshakeValidator
{
    public const int CurrentProtocolVersion = 1;

    public static BridgeHandshakeValidationResult Validate(BridgeMessage? message, BridgeSettings settings)
        => Validate(message, settings, CurrentProtocolVersion);

    public static BridgeHandshakeValidationResult Validate(BridgeMessage? message, BridgeSettings settings, int supportedProtocolVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var envelopeRejection = ValidateEnvelope(message);
        if (envelopeRejection is not null)
            return envelopeRejection;

        var correlationId = message!.Id;
        var (resolvedPayload, rejection) = TryReadPayload(message);
        if (rejection is not null)
            return rejection;

        var payload = resolvedPayload!;
        var contentRejection = ValidatePayloadContent(payload, settings, supportedProtocolVersion, correlationId);
        if (contentRejection is not null)
            return contentRejection;

        return Accept(correlationId: correlationId, payload: payload, acceptPayload: CreateAcceptPayload(payload, settings, supportedProtocolVersion));
    }

    private static BridgeHandshakeValidationResult? ValidateEnvelope(BridgeMessage? message)
    {
        if (message is null)
            return Reject(correlationId: null, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        if (message.Type is not BridgeMessageType.Handshake)
            return Reject(correlationId: message.Id, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        if (string.IsNullOrWhiteSpace(message.Id))
            return Reject(correlationId: null, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        if (message.Payload is not { })
            return Reject(correlationId: message.Id, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        return null;
    }

    private static (BridgeHandshakeClientPayload? Payload, BridgeHandshakeValidationResult? Rejection) TryReadPayload(BridgeMessage message)
    {
        try
        {
            var payload = message.Payload!.Value.Deserialize(BridgeJsonContext.Default.BridgeHandshakeClientPayload);
            return payload is null
                ? (null, Reject(correlationId: message.Id, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null))
                : (payload, null);
        }
        catch (JsonException)
        {
            return (null, Reject(correlationId: message.Id, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null));
        }
    }

    private static BridgeHandshakeValidationResult? ValidatePayloadContent(
        BridgeHandshakeClientPayload payload,
        BridgeSettings settings,
        int supportedProtocolVersion,
        string correlationId)
    {
        if (string.IsNullOrWhiteSpace(payload.SessionId))
            return Reject(correlationId: correlationId, rejectCode: BridgeProtocolErrorCodes.MissingSessionId, rejectPayload: null);

        if (string.IsNullOrWhiteSpace(payload.Secret))
            return Reject(correlationId: correlationId, rejectCode: BridgeProtocolErrorCodes.MissingSecret, rejectPayload: null);

        if (payload.ProtocolVersion <= 0)
            return Reject(correlationId: correlationId, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        if (string.IsNullOrWhiteSpace(payload.BrowserFamily) || string.IsNullOrWhiteSpace(payload.ExtensionVersion))
            return Reject(correlationId: correlationId, rejectCode: BridgeProtocolErrorCodes.InvalidPayload, rejectPayload: null);

        if (!string.Equals(payload.Secret, settings.Secret, StringComparison.Ordinal))
            return Reject(correlationId: correlationId, rejectCode: BridgeProtocolErrorCodes.SecretMismatch, rejectPayload: null);

        if (payload.ProtocolVersion != supportedProtocolVersion)
        {
            return Reject(
                correlationId: correlationId,
                rejectCode: BridgeProtocolErrorCodes.UnsupportedProtocolVersion,
                rejectPayload: new BridgeHandshakeRejectPayload(
                    Retryable: true,
                    SupportedProtocolVersion: supportedProtocolVersion));
        }

        return null;
    }

    private static BridgeHandshakeAcceptPayload CreateAcceptPayload(
        BridgeHandshakeClientPayload payload,
        BridgeSettings settings,
        int supportedProtocolVersion)
        => new(
            SessionId: payload.SessionId,
            NegotiatedProtocolVersion: supportedProtocolVersion,
            RequestTimeoutMs: ToMilliseconds(settings.RequestTimeout),
            PingIntervalMs: ToMilliseconds(settings.PingInterval),
            MaxMessageSize: settings.MaxMessageSize,
            ServerTimeUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static int ToMilliseconds(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            return 0;

        var totalMilliseconds = value.TotalMilliseconds;
        if (totalMilliseconds >= int.MaxValue)
            return int.MaxValue;

        return (int)totalMilliseconds;
    }

    private static BridgeHandshakeValidationResult Accept(
        string correlationId,
        BridgeHandshakeClientPayload payload,
        BridgeHandshakeAcceptPayload acceptPayload)
        => new(
            Outcome: BridgeHandshakeValidationOutcome.Accepted,
            CorrelationId: correlationId,
            ClientPayload: payload,
            AcceptPayload: acceptPayload,
            RejectCode: null,
            RejectPayload: null);

    private static BridgeHandshakeValidationResult Reject(
        string? correlationId,
        string rejectCode,
        BridgeHandshakeRejectPayload? rejectPayload)
        => new(
            Outcome: BridgeHandshakeValidationOutcome.Rejected,
            CorrelationId: correlationId,
            ClientPayload: null,
            AcceptPayload: null,
            RejectCode: rejectCode,
            RejectPayload: rejectPayload);
}