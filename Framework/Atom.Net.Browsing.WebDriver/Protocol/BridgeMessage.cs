using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Сообщение протокола обмена между драйвером и browser bridge.
/// </summary>
internal sealed class BridgeMessage
{
    public required string Id { get; init; }

    public required BridgeMessageType Type { get; init; }

    public string? WindowId { get; init; }

    public string? TabId { get; init; }

    public BridgeCommand? Command { get; init; }

    public BridgeEvent? Event { get; init; }

    public BridgeStatus? Status { get; init; }

    public JsonElement? Payload { get; init; }

    public string? Error { get; init; }

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}