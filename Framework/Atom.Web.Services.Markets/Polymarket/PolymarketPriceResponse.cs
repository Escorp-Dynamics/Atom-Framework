using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Ответ REST API Polymarket с информацией о цене.
/// Используется для эндпоинтов /price, /midpoint, /spread и /tick-size.
/// </summary>
public sealed class PolymarketPriceResponse
{
    /// <summary>
    /// Цена (для /price).
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>
    /// Середина спреда (для /midpoint).
    /// </summary>
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    /// <summary>
    /// Спред (для /spread).
    /// </summary>
    [JsonPropertyName("spread")]
    public string? Spread { get; set; }

    /// <summary>
    /// Минимальный шаг цены (для /tick-size).
    /// </summary>
    [JsonPropertyName("minimum_tick_size")]
    public string? MinimumTickSize { get; set; }
}
