using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Статус ордера в книге ордеров Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketOrderStatus>))]
public enum PolymarketOrderStatus : byte
{
    /// <summary>
    /// Ордер активен и находится в стакане.
    /// </summary>
    [JsonStringEnumMemberName("LIVE")]
    Live,

    /// <summary>
    /// Ордер отменён пользователем.
    /// </summary>
    [JsonStringEnumMemberName("CANCELLED")]
    Cancelled,

    /// <summary>
    /// Ордер полностью исполнен.
    /// </summary>
    [JsonStringEnumMemberName("MATCHED")]
    Matched,

    /// <summary>
    /// Ордер частично исполнен и всё ещё активен.
    /// </summary>
    [JsonStringEnumMemberName("DELAYED")]
    Delayed
}
