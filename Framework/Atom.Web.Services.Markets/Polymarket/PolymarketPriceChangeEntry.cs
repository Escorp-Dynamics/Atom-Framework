using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Отдельное изменение цены в событии обновления стакана.
/// </summary>
public sealed class PolymarketPriceChangeEntry
{
    /// <summary>
    /// Цена уровня.
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; set; }

    /// <summary>
    /// Новый размер на уровне цены (0 означает удаление уровня).
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; set; }

    /// <summary>
    /// Сторона изменения (покупка/продажа).
    /// </summary>
    [JsonPropertyName("side")]
    public required PolymarketSide Side { get; set; }
}
