using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Статус исполнения сделки Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketTradeStatus>))]
public enum PolymarketTradeStatus : byte
{
    /// <summary>
    /// Сделка сопоставлена и ожидает подтверждения.
    /// </summary>
    [JsonStringEnumMemberName("MATCHED")]
    Matched,

    /// <summary>
    /// Сделка подтверждена в блокчейне.
    /// </summary>
    [JsonStringEnumMemberName("CONFIRMED")]
    Confirmed,

    /// <summary>
    /// Сделка отклонена.
    /// </summary>
    [JsonStringEnumMemberName("FAILED")]
    Failed,

    /// <summary>
    /// Сделка отозвана.
    /// </summary>
    [JsonStringEnumMemberName("RETRACTED")]
    Retracted
}
