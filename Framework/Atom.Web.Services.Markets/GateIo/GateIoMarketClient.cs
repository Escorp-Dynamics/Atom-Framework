using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.GateIo;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Gate.io API v4.
// WebSocket (wss://api.gateio.ws/ws/v4/) + REST v4.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Gate.io.
/// </summary>
public sealed class GateIoPriceSnapshot : IMarketPriceSnapshot
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

/// <summary>
/// Позиция на Gate.io.
/// </summary>
public sealed class GateIoPosition : IMarketPosition
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

/// <summary>
/// Сводка портфеля Gate.io.
/// </summary>
public sealed class GateIoPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>
/// Книга ордеров Gate.io.
/// </summary>
public sealed class GateIoOrderBookSnapshot : IMarketOrderBookSnapshot
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

/// <summary>
/// Торговый сигнал Gate.io.
/// </summary>
public sealed class GateIoTradeSignal : IMarketTradeSignal
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

/// <summary>
/// Исключение операций Gate.io.
/// </summary>
public sealed class GateIoException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v4

/// <summary>
/// WebSocket-клиент Gate.io v4.
/// </summary>
/// <remarks>
/// Подключается к wss://api.gateio.ws/ws/v4/
/// Каналы: spot.tickers, spot.order_book, spot.trades.
/// </remarks>
public class GateIoClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый адрес WebSocket API Gate.io.
    /// </summary>
    public const string DefaultWsUrl = "wss://api.gateio.ws/ws/v4/";

    /// <summary>
    /// Создаёт WebSocket-клиент Gate.io.
    /// </summary>
    /// <param name="reconnectDelay">Задержка между попытками переподключения.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. 0 означает без ограничения.</param>
    /// <param name="pingInterval">Интервал отправки ping-сообщений.</param>
    public GateIoClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Gate.io";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("subscribe", marketIds);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("unsubscribe", marketIds);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("event", out var eventProperty))
        {
            var eventName = MarketJsonParsingHelpers.TryGetString(eventProperty);
            if (string.Equals(eventName, "subscribe", StringComparison.OrdinalIgnoreCase))
            {
                var marketIds = ExtractAcknowledgedMarketIds(root);
                if (marketIds.Length > 0)
                    await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

                return;
            }

            if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
            {
                var errorMessage = ExtractErrorMessage(root, out var errorCode);
                await PublishRuntimeErrorAsync(
                    new GateIoException($"Gate.io WebSocket error {errorCode}: {errorMessage}".Trim()))
                    .ConfigureAwait(false);
                return;
            }

            if (!string.Equals(eventName, "update", StringComparison.OrdinalIgnoreCase))
                return;
        }

        if (!MarketJsonParsingHelpers.PropertyEquals(root, "channel", "spot.tickers")
            || !root.TryGetProperty("result", out var resultProperty))
        {
            return;
        }

        if (resultProperty.ValueKind is JsonValueKind.Object)
        {
            await PublishTickerUpdateAsync(resultProperty).ConfigureAwait(false);
            return;
        }

        if (resultProperty.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var tickerElement in resultProperty.EnumerateArray())
            await PublishTickerUpdateAsync(tickerElement).ConfigureAwait(false);
    }

    private async ValueTask PublishTickerUpdateAsync(JsonElement tickerElement)
    {
        if (!tickerElement.TryGetProperty("currency_pair", out var pairProperty))
            return;

        var assetId = MarketJsonParsingHelpers.TryGetString(pairProperty);
        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "highest_bid");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "lowest_ask");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "last");

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

    private static string[] ExtractAcknowledgedMarketIds(JsonElement root)
    {
        if (root.TryGetProperty("payload", out var payloadProperty)
            && payloadProperty.ValueKind is JsonValueKind.Array)
        {
            return payloadProperty.EnumerateArray()
                .Select(MarketJsonParsingHelpers.TryGetString)
                .Where(static marketId => !string.IsNullOrWhiteSpace(marketId))
                .ToArray()!;
        }

        if (root.TryGetProperty("result", out var resultProperty)
            && resultProperty.ValueKind is JsonValueKind.Object
            && resultProperty.TryGetProperty("currency_pair", out var pairProperty))
        {
            var marketId = MarketJsonParsingHelpers.TryGetString(pairProperty);
            return string.IsNullOrWhiteSpace(marketId) ? [] : [marketId];
        }

        return [];
    }

    private static string ExtractErrorMessage(JsonElement root, out string? errorCode)
    {
        if (root.TryGetProperty("error", out var errorProperty)
            && errorProperty.ValueKind is JsonValueKind.Object)
        {
            errorCode = MarketJsonParsingHelpers.TryGetString(errorProperty, "code");
            return MarketJsonParsingHelpers.TryGetString(errorProperty, "message")
                ?? MarketJsonParsingHelpers.TryGetString(root, "message")
                ?? "Unknown Gate.io error";
        }

        errorCode = MarketJsonParsingHelpers.TryGetString(root, "code");
        return MarketJsonParsingHelpers.TryGetString(root, "message")
            ?? MarketJsonParsingHelpers.TryGetString(root, "msg")
            ?? "Unknown Gate.io error";
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string @event, string[] marketIds)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append("{\"time\":");
        builder.Append(timestamp);
        builder.Append(",\"channel\":\"spot.tickers\",\"event\":\"");
        builder.Append(@event);
        builder.Append("\",\"payload\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append(marketIds[index]);
            builder.Append('"');
        }

        builder.Append("]}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API v4

/// <summary>
/// REST-клиент Gate.io API v4.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v4/spot/tickers, GET /api/v4/spot/order_book
/// Приватные: POST /api/v4/spot/orders — HMAC-SHA512 подпись.
/// </remarks>
public sealed class GateIoRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Базовый адрес REST API Gate.io.
    /// </summary>
    public const string DefaultApiUrl = "https://api.gateio.ws";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>
    /// Базовый URL REST API, используемый клиентом.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Gate.io v4.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA512 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public GateIoRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.GateIo(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v4/spot/orders
        const string path = "/api/v4/spot/orders";
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);
        var orderType = price.HasValue ? "limit" : "market";

        var bodyObj = new Dictionary<string, string>
        {
            ["currency_pair"] = assetId,
            ["side"] = sideStr,
            ["type"] = orderType,
            ["amount"] = qtyStr
        };

        if (price.HasValue)
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new GateIoException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GateIoException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("id", out var idProp)
            ? MarketJsonParsingHelpers.TryGetString(idProp) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // DELETE /api/v4/spot/orders/{order_id}?currency_pair=...
        // orderId в формате "PAIR:ORDER_ID" (BTC_USDT:12345)
        var parts = orderId.Split(':', 2);
        var (pair, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new GateIoException("orderId должен быть в формате 'PAIR:ORDER_ID'.");

        var path = $"/api/v4/spot/orders/{Uri.EscapeDataString(oid)}";
        var query = $"currency_pair={Uri.EscapeDataString(pair)}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"{path}?{query}");

        if (authenticator is null)
            throw new GateIoException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v4/spot/tickers?currency_pair=BTC_USDT
        var response = await httpClient.GetAsync(
            $"/api/v4/spot/tickers?currency_pair={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        if (arr.GetArrayLength() == 0) return null;

        return MarketJsonParsingHelpers.TryParseDouble(arr[0], "last");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v4/spot/order_book?currency_pair=BTC_USDT&limit=20
        var response = await httpClient.GetAsync(
            $"/api/v4/spot/order_book?currency_pair={Uri.EscapeDataString(assetId)}&limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static (double Price, double Qty)[] ParseLevels(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var arr)) return [];
            var list = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < list.Length; i++)
            {
                var level = arr[i];
                list[i] = (
                    MarketJsonParsingHelpers.TryParseDouble(level[0]) ?? 0,
                    MarketJsonParsingHelpers.TryParseDouble(level[1]) ?? 0);
            }
            return list;
        }

        return new GateIoOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(root, "bids"),
            Asks = ParseLevels(root, "asks")
        };
    }

    /// <summary>
    /// Освобождает ресурсы REST-клиента.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        if (disposeHttpClient) httpClient.Dispose();
    }
}

#endregion

#region IMarketPriceStream — Кеш цен

/// <summary>
/// Кеш цен Gate.io.
/// </summary>
public sealed class GateIoPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, GateIoPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт пустой поток цен Gate.io.
    /// </summary>
    public GateIoPriceStream()
    {
    }

    /// <summary>
    /// Создаёт поток цен Gate.io и связывает его с runtime-клиентом.
    /// </summary>
    /// <param name="client">Клиент, публикующий обновления рынка.</param>
    public GateIoPriceStream(GateIoClient client)
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
            _ => new GateIoPriceSnapshot
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
    /// Обновляет кеш (вызывается из WebSocket receive loop).
    /// </summary>
    /// <param name="pair">Идентификатор торговой пары.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string pair, double bid, double ask, double lastTrade)
    {
        SetPrice(pair, new GateIoPriceSnapshot
        {
            AssetId = pair,
            BestBid = bid,
            BestAsk = ask,
            LastTradePrice = lastTrade,
            LastUpdateTicks = Environment.TickCount64
        });
    }

    /// <inheritdoc />
    public void ClearCache() => cache.Clear();

    /// <summary>
    /// Освобождает ресурсы потока цен и очищает кеш.
    /// </summary>
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

[JsonSerializable(typeof(GateIoPriceSnapshot))]
[JsonSerializable(typeof(GateIoPosition))]
[JsonSerializable(typeof(GateIoPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class GateIoJsonContext : JsonSerializerContext;

#endregion
