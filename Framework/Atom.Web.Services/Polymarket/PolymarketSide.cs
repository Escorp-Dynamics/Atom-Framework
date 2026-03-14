using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Сторона ордера или сделки (покупка/продажа).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketSide>))]
public enum PolymarketSide : byte
{
    /// <summary>
    /// Покупка.
    /// </summary>
    [JsonStringEnumMemberName("BUY")]
    Buy,

    /// <summary>
    /// Продажа.
    /// </summary>
    [JsonStringEnumMemberName("SELL")]
    Sell
}
