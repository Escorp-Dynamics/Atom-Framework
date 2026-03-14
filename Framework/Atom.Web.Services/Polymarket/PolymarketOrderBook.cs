using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Стакан ордеров, полученный через REST API Polymarket.
/// Отличается от WebSocket-версии именованием полей (bids/asks вместо buys/sells).
/// </summary>
public sealed class PolymarketOrderBook : IMarketOrderBookSnapshot
{
    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; set; }

    /// <summary>
    /// Идентификатор актива (token ID).
    /// </summary>
    [JsonPropertyName("asset_id")]
    public string? AssetId { get; set; }

    /// <summary>
    /// Хеш состояния стакана.
    /// </summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>
    /// Временная метка (UNIX timestamp).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Заявки на покупку (bid), отсортированные по убыванию цены.
    /// </summary>
    [JsonPropertyName("bids")]
    public PolymarketBookEntry[]? Bids { get; set; }

    /// <summary>
    /// Заявки на продажу (ask), отсортированные по возрастанию цены.
    /// </summary>
    [JsonPropertyName("asks")]
    public PolymarketBookEntry[]? Asks { get; set; }

    // IMarketOrderBookSnapshot — явная реализация
    string IMarketOrderBookSnapshot.AssetId => AssetId ?? string.Empty;
    DateTimeOffset IMarketOrderBookSnapshot.Timestamp =>
        long.TryParse(Timestamp, out var ts) ? DateTimeOffset.FromUnixTimeSeconds(ts) : DateTimeOffset.MinValue;
}
