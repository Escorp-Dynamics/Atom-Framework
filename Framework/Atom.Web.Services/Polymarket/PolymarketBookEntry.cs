using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Запись в стакане ордеров Polymarket (уровень цены).
/// </summary>
public sealed class PolymarketBookEntry
{
    /// <summary>
    /// Цена уровня.
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; set; }

    /// <summary>
    /// Суммарный объём на уровне цены.
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; set; }
}
