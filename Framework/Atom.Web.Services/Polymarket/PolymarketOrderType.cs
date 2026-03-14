using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Тип ордера Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketOrderType>))]
public enum PolymarketOrderType : byte
{
    /// <summary>
    /// Good-Til-Cancelled — ордер остаётся активным до отмены.
    /// </summary>
    [JsonStringEnumMemberName("GTC")]
    GoodTilCancelled,

    /// <summary>
    /// Good-Til-Date — ордер остаётся активным до указанной даты.
    /// </summary>
    [JsonStringEnumMemberName("GTD")]
    GoodTilDate,

    /// <summary>
    /// Fill-Or-Kill — ордер исполняется полностью или отменяется.
    /// </summary>
    [JsonStringEnumMemberName("FOK")]
    FillOrKill
}
