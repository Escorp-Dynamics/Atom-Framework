using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Контекст сериализации JSON для моделей Polymarket.
/// Обеспечивает совместимость с NativeAOT через генерацию кода на этапе компиляции.
/// </summary>
[JsonSerializable(typeof(PolymarketMessage))]
[JsonSerializable(typeof(PolymarketSubscription))]
[JsonSerializable(typeof(PolymarketAuth))]
[JsonSerializable(typeof(PolymarketBookSnapshot))]
[JsonSerializable(typeof(PolymarketBookEntry))]
[JsonSerializable(typeof(PolymarketBookEntry[]))]
[JsonSerializable(typeof(PolymarketPriceChange))]
[JsonSerializable(typeof(PolymarketPriceChangeEntry))]
[JsonSerializable(typeof(PolymarketPriceChangeEntry[]))]
[JsonSerializable(typeof(PolymarketLastTradePrice))]
[JsonSerializable(typeof(PolymarketTickSizeChange))]
[JsonSerializable(typeof(PolymarketOrder))]
[JsonSerializable(typeof(PolymarketTrade))]
[JsonSerializable(typeof(PolymarketChannel))]
[JsonSerializable(typeof(PolymarketEventType))]
[JsonSerializable(typeof(PolymarketSide))]
[JsonSerializable(typeof(PolymarketOrderStatus))]
[JsonSerializable(typeof(PolymarketOrderType))]
[JsonSerializable(typeof(PolymarketTradeStatus))]
[JsonSerializable(typeof(PolymarketTraderSide))]
[JsonSerializable(typeof(PolymarketOrder[]))]
[JsonSerializable(typeof(PolymarketTrade[]))]
// REST API модели
[JsonSerializable(typeof(PolymarketMarket))]
[JsonSerializable(typeof(PolymarketMarket[]))]
[JsonSerializable(typeof(PolymarketToken))]
[JsonSerializable(typeof(PolymarketToken[]))]
[JsonSerializable(typeof(PolymarketOrderBook))]
[JsonSerializable(typeof(PolymarketOrderBook[]))]
[JsonSerializable(typeof(PolymarketPriceResponse))]
[JsonSerializable(typeof(PolymarketPriceResponse[]))]
[JsonSerializable(typeof(PolymarketCreateOrderRequest))]
[JsonSerializable(typeof(PolymarketSignedOrder))]
[JsonSerializable(typeof(PolymarketOrderResponse))]
[JsonSerializable(typeof(PolymarketCancelResponse))]
[JsonSerializable(typeof(PolymarketBalanceAllowance))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
internal sealed partial class PolymarketJsonContext : JsonSerializerContext;
