using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Модель сделки Polymarket, получаемая через WebSocket-канал пользователя.
/// </summary>
public sealed class PolymarketTrade
{
    /// <summary>
    /// Уникальный идентификатор сделки.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Идентификатор ордера тейкера.
    /// </summary>
    [JsonPropertyName("taker_order_id")]
    public string? TakerOrderId { get; set; }

    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; set; }

    /// <summary>
    /// Идентификатор актива.
    /// </summary>
    [JsonPropertyName("asset_id")]
    public string? AssetId { get; set; }

    /// <summary>
    /// Сторона сделки (покупка/продажа).
    /// </summary>
    [JsonPropertyName("side")]
    public PolymarketSide Side { get; set; }

    /// <summary>
    /// Объём сделки.
    /// </summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>
    /// Комиссия в базисных пунктах.
    /// </summary>
    [JsonPropertyName("fee_rate_bps")]
    public string? FeeRateBps { get; set; }

    /// <summary>
    /// Цена исполнения сделки.
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>
    /// Статус сделки.
    /// </summary>
    [JsonPropertyName("status")]
    public PolymarketTradeStatus Status { get; set; }

    /// <summary>
    /// Время сопоставления ордеров (UNIX timestamp).
    /// </summary>
    [JsonPropertyName("match_time")]
    public string? MatchTime { get; set; }

    /// <summary>
    /// Время последнего обновления (UNIX timestamp).
    /// </summary>
    [JsonPropertyName("last_update")]
    public string? LastUpdate { get; set; }

    /// <summary>
    /// Исход рынка ("Yes" / "No").
    /// </summary>
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    /// <summary>
    /// Индекс корзины.
    /// </summary>
    [JsonPropertyName("bucket_index")]
    public string? BucketIndex { get; set; }

    /// <summary>
    /// Адрес владельца.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Роль трейдера в сделке (мейкер/тейкер).
    /// </summary>
    [JsonPropertyName("trader_side")]
    public PolymarketTraderSide TraderSide { get; set; }

    /// <summary>
    /// Хеш транзакции в блокчейне.
    /// </summary>
    [JsonPropertyName("transaction_hash")]
    public string? TransactionHash { get; set; }
}
