using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

internal static class BridgeTestHelpers
{
    public static BridgeHandshakeClientPayload CreateClientPayload(
        string sessionId = "session-a",
        string secret = "test-secret",
        int protocolVersion = BridgeHandshakeValidator.CurrentProtocolVersion,
        string browserFamily = "chromium",
        string extensionVersion = "1.0.0")
        => new(
            SessionId: sessionId,
            Secret: secret,
            ProtocolVersion: protocolVersion,
            BrowserFamily: browserFamily,
            ExtensionVersion: extensionVersion);

    public static BridgeMessage CreateHandshakeMessage(BridgeHandshakeClientPayload payload, string? messageId = null)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId,
            Type = BridgeMessageType.Handshake,
            Payload = JsonSerializer.SerializeToElement(payload, BridgeJsonContext.Default.BridgeHandshakeClientPayload),
        };

    public static BridgeMessage CreateEventMessage(BridgeEvent bridgeEvent, string? tabId = null, string? windowId = null, string? messageId = null)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId,
            Type = BridgeMessageType.Event,
            Event = bridgeEvent,
            TabId = tabId,
            WindowId = windowId,
        };

    public static BridgeSettings CreateSettings()
        => new()
        {
            Secret = "test-secret",
            RequestTimeout = TimeSpan.FromSeconds(5),
            PingInterval = TimeSpan.FromSeconds(15),
            MaxMessageSize = 1024,
        };

    public static Uri CreateBridgeUri(BridgeServer server)
        => new($"ws://127.0.0.1:{server.Port}/bridge");

    public static Uri CreateSecureBridgeUri(BridgeServer server, string secret = "test-secret")
        => new($"wss://127.0.0.1:{server.SecureTransportPort}/?secret={Uri.EscapeDataString(secret)}");

    public static async Task SendHandshakeAsync(ClientWebSocket socket, BridgeHandshakeClientPayload payload, string? messageId = null)
    {
        var message = CreateHandshakeMessage(payload, messageId);
        await SendMessageAsync(socket, message).ConfigureAwait(false);
    }

    public static async Task SendMessageAsync(ClientWebSocket socket, BridgeMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJsonContext.Default.BridgeMessage);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
    }

    public static async Task<BridgeMessage?> ReceiveBridgeMessageAsync(ClientWebSocket socket)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
        return result.MessageType is not WebSocketMessageType.Text
            ? null
            : JsonSerializer.Deserialize(buffer.AsSpan(0, result.Count), BridgeJsonContext.Default.BridgeMessage);
    }

    public static async Task<ValueWebSocketReceiveResult> ReceiveCloseAsync(ClientWebSocket socket)
    {
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
        if (result.MessageType is WebSocketMessageType.Close)
            return result;

        Assert.Fail("Expected close frame.");
        return result;
    }

    public static async Task<JsonElement> GetHealthAsync(BridgeServer server)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/health").ConfigureAwait(false);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.Clone();
    }

    public static async Task<JsonElement> WaitForHealthAsync(BridgeServer server, Func<JsonElement, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var health = await GetHealthAsync(server).ConfigureAwait(false);
            if (predicate(health))
                return health;

            await Task.Delay(25).ConfigureAwait(false);
        }

        var finalHealth = await GetHealthAsync(server).ConfigureAwait(false);
        Assert.Fail($"Health predicate was not satisfied. Last snapshot: {finalHealth.GetRawText()}");
        return finalHealth;
    }

    public static async Task WaitForConnectionCountAsync(BridgeServer server, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (server.ConnectionCount == expected)
                return;

            await Task.Delay(25).ConfigureAwait(false);
        }

        Assert.That(server.ConnectionCount, Is.EqualTo(expected));
    }
}