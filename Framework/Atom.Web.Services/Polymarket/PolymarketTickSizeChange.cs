using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Событие изменения минимального шага цены для актива Polymarket.
/// </summary>
public sealed class PolymarketTickSizeChange
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
    /// Предыдущий минимальный шаг цены.
    /// </summary>
    [JsonPropertyName("old_tick_size")]
    public string? OldTickSize { get; set; }

    /// <summary>
    /// Новый минимальный шаг цены.
    /// </summary>
    [JsonPropertyName("new_tick_size")]
    public string? NewTickSize { get; set; }
}
