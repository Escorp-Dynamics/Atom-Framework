using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeHandshakeSkeletonTests
{
    [Test]
    public void HandshakeAcceptsValidClientPayload()
    {
        var result = BridgeHandshakeValidator.Validate(BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(), "handshake-1"), BridgeTestHelpers.CreateSettings());

        Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Accepted));
    }

    [Test]
    public void HandshakeResponseEchoesCorrelationId()
    {
        var message = BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(), "handshake-1");
        var validation = BridgeHandshakeValidator.Validate(message, BridgeTestHelpers.CreateSettings());

        var response = BridgeHandshakeMessageFactory.CreateAcceptMessage(validation);

        Assert.That(response.Id, Is.EqualTo(message.Id));
    }

    [Test]
    public void HandshakeReturnsNegotiatedTransportPolicy()
    {
        var settings = BridgeTestHelpers.CreateSettings();
        var validation = BridgeHandshakeValidator.Validate(BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(), "handshake-1"), settings);

        Assert.Multiple(() =>
        {
            Assert.That(validation.AcceptPayload?.NegotiatedProtocolVersion, Is.EqualTo(BridgeHandshakeValidator.CurrentProtocolVersion));
            Assert.That(validation.AcceptPayload?.RequestTimeoutMs, Is.EqualTo((int)settings.RequestTimeout.TotalMilliseconds));
            Assert.That(validation.AcceptPayload?.PingIntervalMs, Is.EqualTo((int)settings.PingInterval.TotalMilliseconds));
            Assert.That(validation.AcceptPayload?.MaxMessageSize, Is.EqualTo(settings.MaxMessageSize));
        });
    }

    [Test]
    public void HandshakeRejectsNonHandshakeFirstMessage()
    {
        var message = new BridgeMessage
        {
            Id = "handshake-1",
            Type = BridgeMessageType.Request,
        };

        var result = BridgeHandshakeValidator.Validate(message, BridgeTestHelpers.CreateSettings());

        Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
    }

    [Test]
    public void HandshakeRejectsSecretMismatch()
    {
        var result = BridgeHandshakeValidator.Validate(
            BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(secret: "wrong-secret"), "handshake-1"),
            BridgeTestHelpers.CreateSettings());

        Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.SecretMismatch));
    }

    [Test]
    public void HandshakeRejectsUnsupportedProtocolVersion()
    {
        var result = BridgeHandshakeValidator.Validate(
            BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(protocolVersion: 99), "handshake-1"),
            BridgeTestHelpers.CreateSettings(),
            supportedProtocolVersion: BridgeHandshakeValidator.CurrentProtocolVersion);

        Assert.Multiple(() =>
        {
            Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.UnsupportedProtocolVersion));
            Assert.That(result.RejectPayload?.SupportedProtocolVersion, Is.EqualTo(BridgeHandshakeValidator.CurrentProtocolVersion));
        });
    }

    [Test]
    public void HandshakeRejectsMissingSessionId()
    {
        var result = BridgeHandshakeValidator.Validate(
            BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(sessionId: string.Empty), "handshake-1"),
            BridgeTestHelpers.CreateSettings());

        Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.MissingSessionId));
    }

    [Test]
    public void HandshakeRejectsMissingBrowserFamily()
    {
        var result = BridgeHandshakeValidator.Validate(
            BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(browserFamily: string.Empty), "handshake-1"),
            BridgeTestHelpers.CreateSettings());

        Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
    }

    [Test]
    public void HandshakeRejectsMissingExtensionVersion()
    {
        var result = BridgeHandshakeValidator.Validate(
            BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(extensionVersion: string.Empty), "handshake-1"),
            BridgeTestHelpers.CreateSettings());

        Assert.That(result.RejectCode, Is.EqualTo(BridgeProtocolErrorCodes.InvalidPayload));
    }

    [Test]
    public void HandshakeAllowsUnknownOptionalFields()
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

        var result = BridgeHandshakeValidator.Validate(message, BridgeTestHelpers.CreateSettings());

        Assert.That(result.Outcome, Is.EqualTo(BridgeHandshakeValidationOutcome.Accepted));
    }

    [Test]
    public void HandshakeDoesNotLeakSecretInAcceptPayload()
    {
        var validation = BridgeHandshakeValidator.Validate(BridgeTestHelpers.CreateHandshakeMessage(BridgeTestHelpers.CreateClientPayload(), "handshake-1"), BridgeTestHelpers.CreateSettings());
        var response = BridgeHandshakeMessageFactory.CreateAcceptMessage(validation);

        Assert.That(response.Payload?.GetRawText(), Does.Not.Contain("secret"));
    }


}