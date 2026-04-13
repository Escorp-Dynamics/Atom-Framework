using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

internal static class BridgeLifecycleEventMapper
{
    internal static bool TryRead(BridgeMessage message, out BridgeEvent lifecycleEvent, out Uri? url, out string? title)
    {
        ArgumentNullException.ThrowIfNull(message);

        lifecycleEvent = default;
        url = null;
        title = null;

        if (message.Type != BridgeMessageType.Event || message.Event is not BridgeEvent @event)
        {
            return false;
        }

        if (@event is not (BridgeEvent.DomContentLoaded or BridgeEvent.NavigationCompleted or BridgeEvent.PageLoaded))
        {
            return false;
        }

        if (message.Payload is JsonElement payload && payload.ValueKind == JsonValueKind.Object)
        {
            if (TryGetUrlElement(payload, out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out url);
            }

            if (payload.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                title = titleElement.GetString();
            }
        }

        lifecycleEvent = @event;
        return true;
    }

    private static bool TryGetUrlElement(JsonElement payload, out JsonElement urlElement)
    {
        if (payload.TryGetProperty("url", out urlElement))
            return true;

        return payload.TryGetProperty("href", out urlElement);
    }
}