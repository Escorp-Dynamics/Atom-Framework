using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Роль трейдера в сделке Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketTraderSide>))]
public enum PolymarketTraderSide : byte
{
    /// <summary>
    /// Мейкер — выставил ордер в стакан.
    /// </summary>
    [JsonStringEnumMemberName("MAKER")]
    Maker,

    /// <summary>
    /// Тейкер — исполнил существующий ордер из стакана.
    /// </summary>
    [JsonStringEnumMemberName("TAKER")]
    Taker
}
