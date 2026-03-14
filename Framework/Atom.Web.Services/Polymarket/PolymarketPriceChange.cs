using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Событие изменения уровня цены в стакане ордеров Polymarket.
/// </summary>
public sealed class PolymarketPriceChange
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
    /// Список изменений на уровнях цены.
    /// </summary>
    [JsonPropertyName("changes")]
    public PolymarketPriceChangeEntry[]? Changes { get; set; }
}
