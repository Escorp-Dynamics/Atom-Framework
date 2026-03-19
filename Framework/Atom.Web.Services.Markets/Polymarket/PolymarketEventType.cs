using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Тип события, получаемого через WebSocket API Polymarket.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketEventType>))]
public enum PolymarketEventType : byte
{
    /// <summary>
    /// Снимок стакана ордеров (L2).
    /// </summary>
    [JsonStringEnumMemberName("book")]
    Book,

    /// <summary>
    /// Изменение уровня цены в стакане.
    /// </summary>
    [JsonStringEnumMemberName("price_change")]
    PriceChange,

    /// <summary>
    /// Обновление цены последней сделки.
    /// </summary>
    [JsonStringEnumMemberName("last_trade_price")]
    LastTradePrice,

    /// <summary>
    /// Изменение минимального шага цены.
    /// </summary>
    [JsonStringEnumMemberName("tick_size_change")]
    TickSizeChange,

    /// <summary>
    /// Обновление ордера пользователя (канал user).
    /// </summary>
    [JsonStringEnumMemberName("order")]
    Order,

    /// <summary>
    /// Исполнение сделки пользователя (канал user).
    /// </summary>
    [JsonStringEnumMemberName("trade")]
    Trade
}
