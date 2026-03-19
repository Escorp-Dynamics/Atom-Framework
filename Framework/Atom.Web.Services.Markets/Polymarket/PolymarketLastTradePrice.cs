using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Событие обновления цены последней сделки по активу Polymarket.
/// </summary>
public sealed class PolymarketLastTradePrice
{
    /// <summary>
    /// Тип события.
    /// </summary>
    [JsonPropertyName("event_type")]
    public PolymarketEventType EventType { get; set; }

    /// <summary>
    /// Идентификатор актива.
    /// </summary>
    [JsonPropertyName("asset_id")]
    public string? AssetId { get; set; }

    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; set; }

    /// <summary>
    /// Цена последней сделки.
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }
}
