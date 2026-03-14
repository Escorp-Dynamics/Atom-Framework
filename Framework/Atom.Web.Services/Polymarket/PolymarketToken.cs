using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Токен (исход) в рамках рынка Polymarket.
/// </summary>
public sealed class PolymarketToken
{
    /// <summary>
    /// Идентификатор токена.
    /// </summary>
    [JsonPropertyName("token_id")]
    public string? TokenId { get; set; }

    /// <summary>
    /// Исход, которому соответствует токен ("Yes" / "No").
    /// </summary>
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    /// <summary>
    /// Цена токена.
    /// </summary>
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    /// <summary>
    /// Имя победителя (заполняется после разрешения рынка).
    /// </summary>
    [JsonPropertyName("winner")]
    public string? Winner { get; set; }
}
