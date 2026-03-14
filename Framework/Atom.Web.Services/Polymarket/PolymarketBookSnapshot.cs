using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Снимок стакана ордеров (L2) для актива Polymarket.
/// </summary>
public sealed class PolymarketBookSnapshot
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
    /// Временная метка (UNIX timestamp в секундах).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Хеш состояния стакана.
    /// </summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>
    /// Ордера на покупку (bid), отсортированные по убыванию цены.
    /// </summary>
    [JsonPropertyName("buys")]
    public PolymarketBookEntry[]? Buys { get; set; }

    /// <summary>
    /// Ордера на продажу (ask), отсортированные по возрастанию цены.
    /// </summary>
    [JsonPropertyName("sells")]
    public PolymarketBookEntry[]? Sells { get; set; }
}
