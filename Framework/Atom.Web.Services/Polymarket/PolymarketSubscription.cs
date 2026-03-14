using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Сообщение подписки/отписки для WebSocket API Polymarket.
/// </summary>
public sealed class PolymarketSubscription
{
    /// <summary>
    /// Тип действия ("subscribe" или "unsubscribe").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Канал подписки.
    /// </summary>
    [JsonPropertyName("channel")]
    public PolymarketChannel Channel { get; set; }

    /// <summary>
    /// Список идентификаторов рынков (condition ID) для подписки.
    /// </summary>
    [JsonPropertyName("markets")]
    public string[]? Markets { get; set; }

    /// <summary>
    /// Список идентификаторов активов для подписки.
    /// </summary>
    [JsonPropertyName("assets_ids")]
    public string[]? AssetsIds { get; set; }

    /// <summary>
    /// Учётные данные для аутентификации (только для канала user).
    /// </summary>
    [JsonPropertyName("auth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PolymarketAuth? Auth { get; set; }
}
