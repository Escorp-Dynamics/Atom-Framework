using System.Collections.Concurrent;
using System.Globalization;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.CryptoCom;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для Crypto.com Exchange API v1.
// WebSocket (wss://stream.crypto.com/exchange/v1/market) + REST API v1.
// HMAC-SHA256 подпись ордеров.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены Crypto.com.</summary>
public sealed class CryptoComPriceSnapshot : IMarketPriceSnapshot
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double? BestBid { get; set; }

    /// <inheritdoc />
    public double? BestAsk { get; set; }

    /// <inheritdoc />
    public double? Midpoint => (BestBid + BestAsk) / 2.0;

    /// <inheritdoc />
    public double? LastTradePrice { get; set; }

    /// <inheritdoc />
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на Crypto.com.</summary>
public sealed class CryptoComPosition : IMarketPosition
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double Quantity { get; set; }

    /// <inheritdoc />
    public double AverageCostBasis { get; set; }

    /// <inheritdoc />
    public double CurrentPrice { get; set; }

    /// <inheritdoc />
    public double MarketValue => Quantity * CurrentPrice;

    /// <inheritdoc />
    public double UnrealizedPnL => MarketValue - Quantity * AverageCostBasis;

    /// <inheritdoc />
    public double UnrealizedPnLPercent =>
        AverageCostBasis != 0 ? (UnrealizedPnL / (Quantity * AverageCostBasis)) * 100 : 0;

    /// <inheritdoc />
    public double RealizedPnL { get; set; }

    /// <inheritdoc />
    public double TotalFees { get; set; }

    /// <inheritdoc />
    public int TradeCount { get; set; }

    /// <inheritdoc />
    public bool IsClosed => Quantity <= 0;
}

/// <summary>Сводка портфеля Crypto.com.</summary>
public sealed class CryptoComPortfolioSummary : IMarketPortfolioSummary
{
    /// <inheritdoc />
    public int OpenPositions { get; init; }

    /// <inheritdoc />
    public int ClosedPositions { get; init; }

    /// <inheritdoc />
    public double TotalMarketValue { get; init; }

    /// <inheritdoc />
    public double TotalCostBasis { get; init; }

    /// <inheritdoc />
    public double TotalUnrealizedPnL { get; init; }

    /// <inheritdoc />
    public double TotalRealizedPnL { get; init; }

    /// <inheritdoc />
    public double TotalFees { get; init; }

    /// <inheritdoc />
    public double NetPnL => TotalUnrealizedPnL + TotalRealizedPnL - TotalFees;
}

/// <summary>Книга ордеров Crypto.com.</summary>
public sealed class CryptoComOrderBookSnapshot : IMarketOrderBookSnapshot
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <inheritdoc />
    public (double Price, double Quantity)[] Bids { get; init; } = [];

    /// <inheritdoc />
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал Crypto.com.</summary>
public sealed class CryptoComTradeSignal : IMarketTradeSignal
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public TradeAction Action { get; init; }

    /// <inheritdoc />
    public double Quantity { get; init; }

    /// <inheritdoc />
    public string? Price { get; init; }

    /// <inheritdoc />
    public double Confidence { get; init; }

    /// <inheritdoc />
    public string? Reason { get; init; }
}

#endregion

#region Исключение

/// <summary>Исключение операций Crypto.com.</summary>
public sealed class CryptoComException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Crypto.com для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://stream.crypto.com/exchange/v1/market
/// Подписка: { "method":"subscribe","params":{"channels":["ticker.BTC_USDT"]},"id":1 }
/// </remarks>
public class CryptoComClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Crypto.com Exchange.
    /// </summary>
    public const string DefaultWsUrl = "wss://stream.crypto.com/exchange/v1/market";

    /// <summary>
    /// Создаёт WebSocket-клиент Crypto.com для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public CryptoComClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(
            reconnectDelay: reconnectDelay,
            maxReconnectAttempts: maxReconnectAttempts,
            pingInterval: pingInterval == default ? TimeSpan.Zero : pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Crypto.com";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("subscribe", 1, marketIds);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("unsubscribe", 2, marketIds);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (MarketJsonParsingHelpers.PropertyEquals(root, "method", "public/heartbeat"))
        {
            if (root.TryGetProperty("id", out var heartbeatId))
            {
                await SendRuntimeMessageAsync(
                    BuildHeartbeatResponse(heartbeatId),
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (root.TryGetProperty("code", out var codeProperty))
        {
            var code = MarketJsonParsingHelpers.TryGetString(codeProperty);
            if (!string.IsNullOrWhiteSpace(code)
                && !string.Equals(code, "0", StringComparison.OrdinalIgnoreCase))
            {
                var message = MarketJsonParsingHelpers.TryGetString(root, "message")
                    ?? MarketJsonParsingHelpers.TryGetString(root, "method")
                    ?? "Unknown Crypto.com runtime error.";
                await PublishRuntimeErrorAsync(new CryptoComException($"Crypto.com WebSocket error {code}: {message}"))
                    .ConfigureAwait(false);
                return;
            }
        }

        var method = MarketJsonParsingHelpers.TryGetString(root, "method");
        if (!string.Equals(method, "subscribe", StringComparison.OrdinalIgnoreCase))
            return;

        if (!root.TryGetProperty("result", out var resultProperty)
            || resultProperty.ValueKind is not JsonValueKind.Object
            || !MarketJsonParsingHelpers.PropertyEquals(resultProperty, "channel", "ticker"))
        {
            return;
        }

        if (!resultProperty.TryGetProperty("data", out var dataProperty))
        {
            var marketIds = ExtractAcknowledgedMarketIds(resultProperty);
            if (marketIds.Length > 0)
                await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

            return;
        }

        if (dataProperty.ValueKind is not JsonValueKind.Array || dataProperty.GetArrayLength() == 0)
            return;

        var tickerProperty = dataProperty[0];
        if (tickerProperty.ValueKind is not JsonValueKind.Object)
            return;

        var assetId = MarketJsonParsingHelpers.TryGetString(resultProperty, "instrument_name")
            ?? MarketJsonParsingHelpers.TryGetString(tickerProperty, "i")
            ?? ExtractMarketId(MarketJsonParsingHelpers.TryGetString(resultProperty, "subscription"));

        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickerProperty, "b");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickerProperty, "k");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tickerProperty, "a");

        if (!bestBid.HasValue && !bestAsk.HasValue && !lastTrade.HasValue)
            return;

        await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
            assetId,
            bestBid,
            bestAsk,
            lastTrade,
            Environment.TickCount64,
            MarketRealtimeUpdateKind.Ticker)).ConfigureAwait(false);
    }

    private static string[] ExtractAcknowledgedMarketIds(JsonElement resultProperty)
    {
        var assetId = MarketJsonParsingHelpers.TryGetString(resultProperty, "instrument_name")
            ?? ExtractMarketId(MarketJsonParsingHelpers.TryGetString(resultProperty, "subscription"));

        return string.IsNullOrWhiteSpace(assetId) ? [] : [assetId];
    }

    private static string? ExtractMarketId(string? subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription)
            || !subscription.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return subscription["ticker.".Length..];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string method, int id, string[] marketIds)
    {
        var builder = new StringBuilder();
        builder.Append("{\"id\":");
        builder.Append(id);
        builder.Append(",\"method\":\"");
        builder.Append(method);
        builder.Append("\",\"params\":{\"channels\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append("ticker.");
            builder.Append(marketIds[index]);
            builder.Append('"');
        }

        builder.Append("]},\"nonce\":");
        builder.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        builder.Append('}');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static ReadOnlyMemory<byte> BuildHeartbeatResponse(JsonElement heartbeatId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WritePropertyName("id");
        heartbeatId.WriteTo(writer);
        writer.WriteString("method", "public/respond-heartbeat");
        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenMemory;
    }
}

#endregion

#region IMarketRestClient — REST API + HMAC-SHA256

/// <summary>
/// REST-клиент Crypto.com Exchange API v1.
/// </summary>
/// <remarks>
/// Crypto.com: HMAC-SHA256 подпись: method + id + apiKey + params (отсортированные) + nonce.
/// POST /exchange/v1/private/create-order, POST /exchange/v1/private/cancel-order.
/// GET /exchange/v1/public/get-ticker, GET /exchange/v1/public/get-book.
/// </remarks>
public sealed class CryptoComRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API Crypto.com Exchange.
    /// </summary>
    public const string DefaultApiUrl = "https://api.crypto.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>
    /// Базовый URL, используемый для REST-запросов.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Crypto.com.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, который будет использован для запросов.</param>
    /// <param name="apiKey">API-ключ биржи.</param>
    /// <param name="apiSecret">Секрет API для подписи запросов.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public CryptoComRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.CryptoCom(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new CryptoComException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        const string method = "private/create-order";
        var id = 1;
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";

        var parameters = new System.Text.Json.Nodes.JsonObject
        {
            ["instrument_name"] = assetId,
            ["side"] = sideStr,
            ["type"] = orderType,
            ["quantity"] = quantity.ToString("G", CultureInfo.InvariantCulture)
        };

        if (price.HasValue)
            parameters["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyObj = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
            ["nonce"] = nonce
        };

        var bodyJson = bodyObj.ToJsonString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/exchange/v1/{method}");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new CryptoComException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("order_id", out var orderId)
            ? orderId.GetString() : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // Формат orderId: "INSTRUMENT:ORDER_ID"
        var parts = orderId.Split(':', 2);
        if (parts.Length != 2)
            throw new CryptoComException($"orderId в формате INSTRUMENT:ORDER_ID, получено: {orderId}");

        const string method = "private/cancel-order";
        var id = 2;
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var parameters = new System.Text.Json.Nodes.JsonObject
        {
            ["instrument_name"] = parts[0],
            ["order_id"] = parts[1]
        };

        var bodyObj = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
            ["nonce"] = nonce
        };

        var bodyJson = bodyObj.ToJsonString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/exchange/v1/{method}");
        authenticator!.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/exchange/v1/public/get-ticker?instrument_name={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data", out var data)) return null;

        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var ticker = data[0];
            return ticker.TryGetProperty("a", out var ask) && side == TradeSide.Buy ? ask.GetDouble()
                : ticker.TryGetProperty("b", out var bid) ? bid.GetDouble() : null;
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/exchange/v1/public/get-book?instrument_name={Uri.EscapeDataString(assetId)}&depth=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data", out var data)) return null;

        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;
        var book = data[0];

        static (double Price, double Qty)[] ParseLevels(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var arr)) return [];
            var list = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < list.Length; i++)
            {
                var level = arr[i];
                list[i] = (level[0].GetDouble(), level[1].GetDouble());
            }
            return list;
        }

        return new CryptoComOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(book, "bids"),
            Asks = ParseLevels(book, "asks")
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        if (disposeHttpClient) httpClient.Dispose();
    }
}

#endregion

#region IMarketPriceStream — Кеш цен

/// <summary>Кеш цен Crypto.com.</summary>
public sealed class CryptoComPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, CryptoComPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Crypto.com без runtime-подписки.
    /// </summary>
    public CryptoComPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Crypto.com и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Crypto.com, публикующий обновления рынка.</param>
    public CryptoComPriceStream(CryptoComClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        runtimeBridge = new MarketRuntimePriceStreamBridge(client, this);
    }

    /// <inheritdoc />
    public int TokenCount => cache.Count;

    /// <inheritdoc />
    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <inheritdoc />
    public void SetPrice(string assetId, IMarketPriceSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(snapshot);

        cache.AddOrUpdate(assetId,
            _ => new CryptoComPriceSnapshot
            {
                AssetId = assetId,
                BestBid = snapshot.BestBid,
                BestAsk = snapshot.BestAsk,
                LastTradePrice = snapshot.LastTradePrice,
                LastUpdateTicks = snapshot.LastUpdateTicks
            },
            (_, existing) =>
            {
                existing.BestBid = snapshot.BestBid;
                existing.BestAsk = snapshot.BestAsk;
                existing.LastTradePrice = snapshot.LastTradePrice;
                existing.LastUpdateTicks = snapshot.LastUpdateTicks;
                return existing;
            });
    }

    /// <summary>
    /// Обновляет кеш на основании нового ticker update от Crypto.com.
    /// </summary>
    /// <param name="symbol">Идентификатор рынка.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new CryptoComPriceSnapshot
        {
            AssetId = symbol,
            BestBid = bid,
            BestAsk = ask,
            LastTradePrice = lastTrade,
            LastUpdateTicks = Environment.TickCount64
        });
    }

    /// <inheritdoc />
    public void ClearCache() => cache.Clear();

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        runtimeBridge?.Dispose();
        cache.Clear();
    }
}

#endregion

#region Source-Generated JSON

[JsonSerializable(typeof(CryptoComPriceSnapshot))]
[JsonSerializable(typeof(CryptoComPosition))]
[JsonSerializable(typeof(CryptoComPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CryptoComJsonContext : JsonSerializerContext;

#endregion
