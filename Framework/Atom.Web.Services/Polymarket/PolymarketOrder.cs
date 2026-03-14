using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Модель ордера Polymarket, получаемая через WebSocket-канал пользователя.
/// </summary>
public sealed class PolymarketOrder
{
    /// <summary>
    /// Уникальный идентификатор ордера.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

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
    /// Сторона ордера (покупка/продажа).
    /// </summary>
    [JsonPropertyName("side")]
    public PolymarketSide Side { get; set; }

    /// <summary>
    /// Тип ордера (GTC, GTD, FOK).
    /// </summary>
    [JsonPropertyName("type")]
    public PolymarketOrderType Type { get; set; }

    /// <summary>
    /// Исходный размер ордера.
    /// </summary>
    [JsonPropertyName("original_size")]
    public string? OriginalSize { get; set; }

    /// <summary>
    /// Исполненный объём ордера.
    /// </summary>
    [JsonPropertyName("size_matched")]
    public string? SizeMatched { get; set; }

    /// <summary>
    /// Цена ордера.
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>
    /// Текущий статус ордера.
    /// </summary>
    [JsonPropertyName("status")]
    public PolymarketOrderStatus Status { get; set; }

    /// <summary>
    /// Адрес владельца ордера.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Временная метка создания ордера (UNIX timestamp).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Дата истечения ордера (UNIX timestamp, "0" = бессрочный).
    /// </summary>
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    /// <summary>
    /// Идентификаторы связанных сделок.
    /// </summary>
    [JsonPropertyName("associate_trades")]
    public string[]? AssociateTrades { get; set; }
}
