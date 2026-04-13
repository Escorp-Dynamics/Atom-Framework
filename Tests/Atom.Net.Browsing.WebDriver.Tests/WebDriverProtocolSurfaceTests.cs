using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverProtocolSurfaceTests
{
    [Test]
    public void BridgeMessageSerializesAndDeserializesWithSourceGeneratedContext()
    {
        var payload = JsonDocument.Parse("{\"script\":\"document.title\"}").RootElement.Clone();
        var message = new BridgeMessage
        {
            Id = "msg-1",
            Type = BridgeMessageType.Request,
            WindowId = "window-1",
            TabId = "tab-1",
            Command = BridgeCommand.ExecuteScript,
            Payload = payload,
            Timestamp = 1234567890,
        };

        var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);
        var roundtrip = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeMessage);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"type\":\"Request\""));
            Assert.That(json, Does.Contain("\"command\":\"ExecuteScript\""));
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Id, Is.EqualTo("msg-1"));
            Assert.That(roundtrip.Type, Is.EqualTo(BridgeMessageType.Request));
            Assert.That(roundtrip.WindowId, Is.EqualTo("window-1"));
            Assert.That(roundtrip.TabId, Is.EqualTo("tab-1"));
            Assert.That(roundtrip.Command, Is.EqualTo(BridgeCommand.ExecuteScript));
            Assert.That(roundtrip.Payload?.GetProperty("script").GetString(), Is.EqualTo("document.title"));
        });
    }

    [Test]
    public void BridgeResponseMessageOmitsNullMembersAndUsesStringEnums()
    {
        var message = new BridgeMessage
        {
            Id = "msg-response",
            Type = BridgeMessageType.Response,
            Status = BridgeStatus.Ok,
            Timestamp = 42,
        };

        var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"type\":\"Response\""));
            Assert.That(json, Does.Contain("\"status\":\"Ok\""));
            Assert.That(json, Does.Not.Contain("\"command\":"));
            Assert.That(json, Does.Not.Contain("\"event\":"));
            Assert.That(json, Does.Not.Contain("\"payload\":"));
            Assert.That(json, Does.Not.Contain("\"error\":"));
            Assert.That(json, Does.Not.Contain("\"windowId\":"));
            Assert.That(json, Does.Not.Contain("\"tabId\":"));
        });
    }

    [Test]
    public void BridgeEventMessageRoundTripsWithPayload()
    {
        var payload = JsonDocument.Parse("{\"url\":\"https://example.test\",\"ready\":true}").RootElement.Clone();
        var message = new BridgeMessage
        {
            Id = "msg-event",
            Type = BridgeMessageType.Event,
            WindowId = "window-event",
            TabId = "tab-event",
            Event = BridgeEvent.NavigationCompleted,
            Payload = payload,
            Timestamp = 99,
        };

        var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);
        var roundtrip = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeMessage);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"type\":\"Event\""));
            Assert.That(json, Does.Contain("\"event\":\"NavigationCompleted\""));
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Event, Is.EqualTo(BridgeEvent.NavigationCompleted));
            Assert.That(roundtrip.WindowId, Is.EqualTo("window-event"));
            Assert.That(roundtrip.TabId, Is.EqualTo("tab-event"));
            Assert.That(roundtrip.Payload?.GetProperty("url").GetString(), Is.EqualTo("https://example.test"));
            Assert.That(roundtrip.Payload?.GetProperty("ready").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void BridgeHandshakeMessageRoundTripsWithoutCommandEnvelope()
    {
        var message = new BridgeMessage
        {
            Id = "msg-handshake",
            Type = BridgeMessageType.Handshake,
            WindowId = "window-7",
            TabId = "7:42",
            Timestamp = 77,
        };

        var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);
        var roundtrip = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeMessage);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"type\":\"Handshake\""));
            Assert.That(json, Does.Not.Contain("\"command\":"));
            Assert.That(json, Does.Not.Contain("\"event\":"));
            Assert.That(json, Does.Not.Contain("\"status\":"));
            Assert.That(roundtrip, Is.Not.Null);
            Assert.That(roundtrip!.Type, Is.EqualTo(BridgeMessageType.Handshake));
            Assert.That(roundtrip.WindowId, Is.EqualTo("window-7"));
            Assert.That(roundtrip.TabId, Is.EqualTo("7:42"));
        });
    }

    [Test]
    public void BridgeSettingsExposeReferenceAlignedDefaults()
    {
        var settings = new BridgeSettings
        {
            Secret = "bridge-secret",
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(settings.Port, Is.Zero);
            Assert.That(settings.Secret, Is.EqualTo("bridge-secret"));
            Assert.That(settings.RequestTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(settings.PingInterval, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(settings.MaxMessageSize, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(settings.AutoCreateVirtualDisplay, Is.True);
        });
    }

    [Test]
    public void WebWindowSurfaceExposesActivationAndCloseOperations()
    {
        var contractType = typeof(IWebWindow);
        var windowType = typeof(WebWindow);
        var activateContract = contractType.GetMethod(nameof(IWebWindow.ActivateAsync), [typeof(CancellationToken)]);
        var closeContract = contractType.GetMethod(nameof(IWebWindow.CloseAsync), [typeof(CancellationToken)]);
        var activateRuntime = windowType.GetMethod(nameof(WebWindow.ActivateAsync), [typeof(CancellationToken)]);
        var closeRuntime = windowType.GetMethod(nameof(WebWindow.CloseAsync), [typeof(CancellationToken)]);

        Assert.Multiple(() =>
        {
            Assert.That(activateContract, Is.Not.Null);
            Assert.That(closeContract, Is.Not.Null);
            Assert.That(activateRuntime, Is.Not.Null);
            Assert.That(closeRuntime, Is.Not.Null);
            Assert.That(activateContract?.ReturnType, Is.EqualTo(typeof(ValueTask)));
            Assert.That(closeContract?.ReturnType, Is.EqualTo(typeof(ValueTask)));
            Assert.That(activateRuntime?.ReturnType, Is.EqualTo(typeof(ValueTask)));
            Assert.That(closeRuntime?.ReturnType, Is.EqualTo(typeof(ValueTask)));
        });
    }
}