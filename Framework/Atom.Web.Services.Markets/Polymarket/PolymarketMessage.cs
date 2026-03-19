using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Базовое входящее сообщение из WebSocket API Polymarket.
/// Используется для первичного определения типа события перед типизированной десериализацией.
/// </summary>
public sealed class PolymarketMessage
{
    /// <summary>
    /// Тип события.
    /// </summary>
    [JsonPropertyName("event_type")]
    public PolymarketEventType EventType { get; set; }

    /// <summary>
    /// Идентификатор актива (присутствует в рыночных событиях).
    /// </summary>
    [JsonPropertyName("asset_id")]
    public string? AssetId { get; set; }

    /// <summary>
    /// Идентификатор рынка / condition ID.
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; set; }

    /// <summary>
    /// Цена (для событий last_trade_price).
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>
    /// Временная метка (UNIX timestamp).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Хеш стакана (для событий book).
    /// </summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>
    /// Заявки на покупку (для событий book).
    /// </summary>
    [JsonPropertyName("buys")]
    public PolymarketBookEntry[]? Buys { get; set; }

    /// <summary>
    /// Заявки на продажу (для событий book).
    /// </summary>
    [JsonPropertyName("sells")]
    public PolymarketBookEntry[]? Sells { get; set; }

    /// <summary>
    /// Изменения уровней цены (для событий price_change).
    /// </summary>
    [JsonPropertyName("changes")]
    public PolymarketPriceChangeEntry[]? Changes { get; set; }

    /// <summary>
    /// Предыдущий шаг цены (для событий tick_size_change).
    /// </summary>
    [JsonPropertyName("old_tick_size")]
    public string? OldTickSize { get; set; }

    /// <summary>
    /// Новый шаг цены (для событий tick_size_change).
    /// </summary>
    [JsonPropertyName("new_tick_size")]
    public string? NewTickSize { get; set; }

    /// <summary>
    /// Данные ордера (для событий order в канале user).
    /// </summary>
    [JsonPropertyName("order")]
    public PolymarketOrder? Order { get; set; }

    /// <summary>
    /// Данные сделки (для событий trade в канале user).
    /// </summary>
    [JsonPropertyName("trade")]
    public PolymarketTrade? Trade { get; set; }

    /// <summary>
    /// Необработанный JSON-элемент для доступа ко всем полям сообщения.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
