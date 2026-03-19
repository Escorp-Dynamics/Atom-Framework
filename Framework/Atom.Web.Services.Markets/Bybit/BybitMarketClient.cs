using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bybit;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Bybit v5 API.
// WebSocket (wss://stream.bybit.com/v5/public/spot) + REST v5.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Bybit.
/// </summary>
public sealed class BybitPriceSnapshot : IMarketPriceSnapshot
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
/// Позиция на Bybit.
/// </summary>
public sealed class BybitPosition : IMarketPosition
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
/// Сводка портфеля Bybit.
/// </summary>
public sealed class BybitPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Bybit.
/// </summary>
public sealed class BybitOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал Bybit.
/// </summary>
public sealed class BybitTradeSignal : IMarketTradeSignal
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
/// Исключение операций Bybit.
/// </summary>
public sealed class BybitException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v5

/// <summary>
/// WebSocket-клиент Bybit v5 для получения рыночных данных.
/// </summary>
/// <remarks>
/// Подключается к wss://stream.bybit.com/v5/public/spot
/// Топики: tickers.{symbol}, orderbook.{depth}.{symbol}, trade.{symbol}
/// </remarks>
public class BybitClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Bybit v5 Spot.
    /// </summary>
    public const string DefaultWsUrl = "wss://stream.bybit.com/v5/public/spot";

    /// <summary>
    /// Создаёт WebSocket-клиент Bybit v5 для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public BybitClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Bybit";

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

        if (root.TryGetProperty("op", out var opProperty))
        {
            var operation = MarketJsonParsingHelpers.TryGetString(opProperty);
            if ((string.Equals(operation, "subscribe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(operation, "unsubscribe", StringComparison.OrdinalIgnoreCase))
                && root.TryGetProperty("success", out var successProperty)
                && successProperty.ValueKind is JsonValueKind.True)
            {
                var marketIds = ExtractAcknowledgedMarketIds(root);
                if (marketIds.Length > 0 && string.Equals(operation, "subscribe", StringComparison.OrdinalIgnoreCase))
                    await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

                return;
            }

            if (root.TryGetProperty("success", out successProperty)
                && successProperty.ValueKind is JsonValueKind.False)
            {
                var retCode = MarketJsonParsingHelpers.TryGetString(root, "retCode");
                var retMessage = MarketJsonParsingHelpers.TryGetString(root, "retMsg") ?? operation ?? "error";
                await PublishRuntimeErrorAsync(new BybitException($"Bybit WebSocket {operation} {retCode}: {retMessage}".Trim())).ConfigureAwait(false);
                return;
            }
        }

        if (!root.TryGetProperty("topic", out var topicProperty))
            return;

        var topic = MarketJsonParsingHelpers.TryGetString(topicProperty);
        if (string.IsNullOrWhiteSpace(topic)
            || !topic.StartsWith("tickers.", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var assetId = MarketJsonParsingHelpers.TryGetString(dataProperty, "symbol");
        if (string.IsNullOrWhiteSpace(assetId))
            assetId = topic["tickers.".Length..];

        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "bid1Price");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "ask1Price");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "lastPrice");

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
        if (!root.TryGetProperty("args", out var argsProperty)
            || argsProperty.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return argsProperty.EnumerateArray()
            .Select(MarketJsonParsingHelpers.TryGetString)
            .Where(static topic => !string.IsNullOrWhiteSpace(topic) && topic.StartsWith("tickers.", StringComparison.OrdinalIgnoreCase))
            .Select(static topic => topic!["tickers.".Length..])
            .ToArray();
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string operation, string[] marketIds)
    {
        var builder = new StringBuilder();
        builder.Append("{\"op\":\"");
        builder.Append(operation);
        builder.Append("\",\"args\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append("tickers.");
            builder.Append(marketIds[index]);
            builder.Append('"');
        }

        builder.Append("]}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API v5

/// <summary>
/// REST-клиент Bybit API v5.
/// </summary>
/// <remarks>
/// Публичные: GET /v5/market/tickers, GET /v5/market/orderbook
/// Приватные: POST /v5/order/create — HMAC-SHA256 подпись.
/// </remarks>
public sealed class BybitRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API Bybit v5.
    /// </summary>
    public const string DefaultApiUrl = "https://api.bybit.com";
    private const int RecvWindow = 5000;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Базовый URL, используемый для REST-запросов.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Создаёт REST-клиент Bybit v5.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public BybitRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bybit(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /v5/order/create
        var sideStr = side == TradeSide.Buy ? "Buy" : "Sell";
        var orderType = price.HasValue ? "Limit" : "Market";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);

        var bodyObj = new Dictionary<string, string>
        {
            ["category"] = "spot",
            ["symbol"] = assetId,
            ["side"] = sideStr,
            ["orderType"] = orderType,
            ["qty"] = qtyStr
        };

        if (price.HasValue)
        {
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);
            bodyObj["timeInForce"] = "GTC";
        }

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v5/order/create")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new BybitException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BybitException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("orderId", out var orderId)
                ? MarketJsonParsingHelpers.TryGetString(orderId) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /v5/order/cancel
        // orderId в формате "SYMBOL:ORDER_ID"
        var parts = orderId.Split(':', 2);
        var (symbol, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new BybitException("orderId должен быть в формате 'SYMBOL:ORDER_ID'.");

        var bodyObj = new Dictionary<string, string>
        {
            ["category"] = "spot",
            ["symbol"] = symbol,
            ["orderId"] = oid
        };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v5/order/cancel")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new BybitException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /v5/market/tickers?category=spot&symbol=BTCUSDT
        var response = await httpClient.GetAsync(
            $"/v5/market/tickers?category=spot&symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
        if (!result.TryGetProperty("list", out var list) || list.GetArrayLength() == 0) return null;

        return MarketJsonParsingHelpers.TryParseDouble(list[0], "lastPrice");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /v5/market/orderbook?category=spot&symbol=BTCUSDT&limit=20
        var response = await httpClient.GetAsync(
            $"/v5/market/orderbook?category=spot&symbol={Uri.EscapeDataString(assetId)}&limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

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

        var tsMs = MarketJsonParsingHelpers.TryParseInt64(result, "ts") ?? 0;

        return new BybitOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs),
            Bids = ParseLevels(result, "b"),
            Asks = ParseLevels(result, "a")
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

/// <summary>
/// Кеш цен Bybit.
/// </summary>
public sealed class BybitPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BybitPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Bybit без runtime-подписки.
    /// </summary>
    public BybitPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Bybit и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Bybit, публикующий обновления рынка.</param>
    public BybitPriceStream(BybitClient client)
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
            _ => new BybitPriceSnapshot
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
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new BybitPriceSnapshot
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

[JsonSerializable(typeof(BybitPriceSnapshot))]
[JsonSerializable(typeof(BybitPosition))]
[JsonSerializable(typeof(BybitPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BybitJsonContext : JsonSerializerContext;

#endregion
