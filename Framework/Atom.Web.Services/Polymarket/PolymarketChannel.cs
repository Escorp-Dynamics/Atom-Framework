using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Канал подписки WebSocket API Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketChannel>))]
public enum PolymarketChannel : byte
{
    /// <summary>
    /// Канал рыночных данных (стакан, цены, сделки).
    /// </summary>
    [JsonStringEnumMemberName("market")]
    Market,

    /// <summary>
    /// Канал пользовательских данных (ордера, трейды). Требует аутентификации.
    /// </summary>
    [JsonStringEnumMemberName("user")]
    User
}
