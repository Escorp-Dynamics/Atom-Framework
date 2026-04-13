using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeHandshakeValidatorTests
{
    [Test]
    public void ValidateAcceptsValidClientPayload()
    {
        var settings = CreateSettings();
        var message = CreateHandshakeMessage(new BridgeHandshakeClientPayload(
            SessionId: "session-a",
            Secret: "test-secret",
            ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
            BrowserFamily: "chromium",
            ExtensionVersion: "1.0.0"));

        var result = BridgeHandshakeValidator.Validate(message, settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Accepted));
            Assert.That(result.CorrelationId, Is.EqualTo(message.Id));
            Assert.That(result.ClientPayload?.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.AcceptPayload?.NegotiatedProtocolVersion, Is.EqualTo(BridgeHandshakeValidator.CurrentProtocolVersion));
            Assert.That(result.AcceptPayload?.RequestTimeoutMs, Is.EqualTo((int)settings.RequestTimeout.TotalMilliseconds));
            Assert.That(result.AcceptPayload?.PingIntervalMs, Is.EqualTo((int)settings.PingInterval.TotalMilliseconds));
            Assert.That(result.AcceptPayload?.MaxMessageSize, Is.EqualTo(settings.MaxMessageSize));
            Assert.That(result.RejectCode, Is.Null);
        });
    }

    [Test]
    public void ValidateRejectsMissingSecret()
    {
        var result = BridgeHandshakeValidator.Validate(
            CreateHandshakeMessage(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: string.Empty,
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0")),
            CreateSettings());

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Rejected));
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.MissingSecret));
        });
    }

    [Test]
    public void ValidateRejectsSecretMismatch()
    {
        var result = BridgeHandshakeValidator.Validate(
            CreateHandshakeMessage(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "wrong-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0")),
            CreateSettings());

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Rejected));
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.SecretMismatch));
        });
    }

    [Test]
    public void ValidateRejectsUnsupportedProtocolVersion()
    {
        var result = BridgeHandshakeValidator.Validate(
            CreateHandshakeMessage(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "test-secret",
                ProtocolVersion: 99,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0")),
            CreateSettings(),
            supportedProtocolVersion: 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Rejected));
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.UnsupportedProtocolVersion));
            Assert.That(result.RejectPayload?.Retryable, Is.True);
            Assert.That(result.RejectPayload?.SupportedProtocolVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public void ValidateRejectsMissingSessionId()
    {
        var result = BridgeHandshakeValidator.Validate(
            CreateHandshakeMessage(new BridgeHandshakeClientPayload(
                SessionId: string.Empty,
                Secret: "test-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: "chromium",
                ExtensionVersion: "1.0.0")),
            CreateSettings());

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Rejected));
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.MissingSessionId));
        });
    }

    [Test]
    public void ValidateRejectsMissingBrowserFamilyAsInvalidPayload()
    {
        var result = BridgeHandshakeValidator.Validate(
            CreateHandshakeMessage(new BridgeHandshakeClientPayload(
                SessionId: "session-a",
                Secret: "test-secret",
                ProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion,
                BrowserFamily: string.Empty,
                ExtensionVersion: "1.0.0")),
            CreateSettings());

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Rejected));
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
        });
    }

    [Test]
    public void ValidateAllowsUnknownOptionalFields()
    {
        var payload = new JsonObject
        {
            ["sessionId"] = "session-a",
            ["secret"] = "test-secret",
            ["protocolVersion"] = BridgeHandshakeValidator.CurrentProtocolVersion,
            ["browserFamily"] = "chromium",
            ["extensionVersion"] = "1.0.0",
            ["unknownField"] = "keep-forward-compatible",
        };

        var message = new BridgeMessage
        {
            Id = "handshake-1",
            Type = BridgeMessageType.Handshake,
            Payload = JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.JsonObject),
        };

        var result = BridgeHandshakeValidator.Validate(message, CreateSettings());

        Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Accepted));
    }

    private static BridgeMessage CreateHandshakeMessage(BridgeHandshakeClientPayload payload)
        => new()
        {
            Id = "handshake-1",
            Type = BridgeMessageType.Handshake,
            Payload = JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.BridgeHandshakeClientPayload),
        };

    private static BridgeSettings CreateSettings()
        => new()
        {
            Secret = "test-secret",
            RequestTimeout = TimeSpan.FromSeconds(5),
            PingInterval = TimeSpan.FromSeconds(15),
            MaxMessageSize = 1024,
        };
}