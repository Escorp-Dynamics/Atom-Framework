using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.KuCoin;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для KuCoin Spot API v1/v3.
// WebSocket (wss://ws-api-spot.kucoin.com) + REST API.
// HMAC-SHA256 подпись ордеров.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены KuCoin.</summary>
public sealed class KuCoinPriceSnapshot : IMarketPriceSnapshot
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

/// <summary>Позиция на KuCoin.</summary>
public sealed class KuCoinPosition : IMarketPosition
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

/// <summary>Сводка портфеля KuCoin.</summary>
public sealed class KuCoinPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров KuCoin.</summary>
public sealed class KuCoinOrderBookSnapshot : IMarketOrderBookSnapshot
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

/// <summary>Торговый сигнал KuCoin.</summary>
public sealed class KuCoinTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций KuCoin.</summary>
public sealed class KuCoinException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент KuCoin для рыночных данных.
/// </summary>
/// <remarks>
/// Требуется запрос POST /api/v1/bullet-public для получения WS-токена.
/// Топики: /market/ticker:{symbol}, /market/level2:{symbol}.
/// </remarks>
public class KuCoinClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint KuCoin Spot.
    /// </summary>
    public const string DefaultWsUrl = "wss://ws-api-spot.kucoin.com";

    private static readonly HttpClient bootstrapHttpClient = new();

    private readonly ConcurrentDictionary<string, KuCoinPendingAckRequest> pendingAckRequests = new(StringComparer.Ordinal);
    private int nextRequestId;

    /// <summary>
    /// Создаёт WebSocket-клиент KuCoin для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public KuCoinClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "KuCoin";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override async ValueTask<Uri> ResolveEndpointUriAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{KuCoinRestClient.DefaultApiUrl}/api/v1/bullet-public");
        using var response = await bootstrapHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new KuCoinException($"KuCoin bullet-public failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var code = MarketJsonParsingHelpers.TryGetString(root, "code");
        if (!string.Equals(code, "200000", StringComparison.Ordinal)
            || !root.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Object)
        {
            throw new KuCoinException($"KuCoin bullet-public returned invalid payload: {json}");
        }

        var token = MarketJsonParsingHelpers.TryGetString(dataProperty, "token");
        if (string.IsNullOrWhiteSpace(token)
            || !dataProperty.TryGetProperty("instanceServers", out var serversProperty)
            || serversProperty.ValueKind is not JsonValueKind.Array)
        {
            throw new KuCoinException($"KuCoin bullet-public missing token or instance server: {json}");
        }

        foreach (var serverProperty in serversProperty.EnumerateArray())
        {
            var endpoint = MarketJsonParsingHelpers.TryGetString(serverProperty, "endpoint");
            if (!string.IsNullOrWhiteSpace(endpoint))
                return BuildBootstrapEndpoint(endpoint, token!);
        }

        throw new KuCoinException($"KuCoin bullet-public returned no usable instance server: {json}");
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return BuildTrackedCommandMessage("subscribe", marketIds, KuCoinPendingAckRequestKind.Subscribe);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        return BuildTrackedCommandMessage("unsubscribe", marketIds, KuCoinPendingAckRequestKind.Unsubscribe);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildPingMessage()
    {
        var requestId = Guid.NewGuid().ToString("N");
        return Encoding.UTF8.GetBytes($"{{\"id\":\"{requestId}\",\"type\":\"ping\"}}");
    }

    /// <inheritdoc />
    protected override WebSocketMessageType PingMessageType => WebSocketMessageType.Text;

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var type = MarketJsonParsingHelpers.TryGetString(root, "type");
        if (string.Equals(type, "ack", StringComparison.OrdinalIgnoreCase)
            && TryGetRequestId(root, out var ackRequestId)
            && pendingAckRequests.TryRemove(ackRequestId, out var ackRequest)
            && ackRequest.Kind is KuCoinPendingAckRequestKind.Subscribe)
        {
            await PublishSubscriptionAcknowledgedAsync(ackRequest.MarketIds, isResubscription: false).ConfigureAwait(false);
            return;
        }

        if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
        {
            var message = MarketJsonParsingHelpers.TryGetString(root, "data")
                ?? MarketJsonParsingHelpers.TryGetString(root, "msg")
                ?? "Unknown KuCoin runtime error.";
            var code = MarketJsonParsingHelpers.TryGetString(root, "code");

            await PublishRuntimeErrorAsync(new KuCoinException($"KuCoin WebSocket error {code}: {message}".Trim())).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(type, "message", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("topic", out var topicProperty)
            || !root.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var assetId = ExtractMarketId(topicProperty);
        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "bestBid");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "bestAsk");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "price");

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

    private static bool TryGetRequestId(JsonElement root, out string requestId)
    {
        requestId = string.Empty;
        if (!root.TryGetProperty("id", out var idProperty))
            return false;

        requestId = MarketJsonParsingHelpers.TryGetString(idProperty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(requestId);
    }

    private ReadOnlyMemory<byte> BuildTrackedCommandMessage(
        string type,
        string[] marketIds,
        KuCoinPendingAckRequestKind requestKind)
    {
        var requestId = Interlocked.Increment(ref nextRequestId).ToString(CultureInfo.InvariantCulture);
        pendingAckRequests[requestId] = new KuCoinPendingAckRequest(requestKind, [.. marketIds]);
        return BuildCommandMessage(requestId, type, marketIds);
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string requestId, string type, string[] marketIds)
    {
        var builder = new StringBuilder();
        builder.Append("{\"id\":\"");
        builder.Append(requestId);
        builder.Append("\",\"type\":\"");
        builder.Append(type);
        builder.Append("\",\"topic\":\"/market/ticker:");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append(marketIds[index]);
        }

        builder.Append("\"}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string? ExtractMarketId(JsonElement topicProperty)
    {
        var topic = MarketJsonParsingHelpers.TryGetString(topicProperty);
        if (string.IsNullOrWhiteSpace(topic)
            || !topic.StartsWith("/market/ticker:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = topic["/market/ticker:".Length..];
        var separatorIndex = value.IndexOf(',');
        return separatorIndex >= 0 ? value[..separatorIndex] : value;
    }

    private static Uri BuildBootstrapEndpoint(string endpoint, string token)
    {
        var builder = new UriBuilder(endpoint);
        var query = builder.Query;
        if (!string.IsNullOrWhiteSpace(query))
            query = query.TrimStart('?') + "&";

        builder.Query = $"{query}token={Uri.EscapeDataString(token)}&connectId={Guid.NewGuid():N}";
        return builder.Uri;
    }

    private enum KuCoinPendingAckRequestKind : byte
    {
        Subscribe,
        Unsubscribe
    }

    private readonly record struct KuCoinPendingAckRequest(KuCoinPendingAckRequestKind Kind, string[] MarketIds);
}

#endregion

#region IMarketRestClient — REST API + HMAC-SHA256

/// <summary>
/// REST-клиент KuCoin Spot API.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v1/market/orderbook/level2_20, GET /api/v1/market/stats
/// Приватные: POST /api/v1/orders — HMAC-SHA256 + passphrase подпись.
/// </remarks>
public sealed class KuCoinRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API KuCoin Spot.
    /// </summary>
    public const string DefaultApiUrl = "https://api.kucoin.com";

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
    /// Создаёт REST-клиент KuCoin.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, который будет использован для запросов.</param>
    /// <param name="apiKey">API-ключ биржи.</param>
    /// <param name="apiSecret">Секрет API для подписи запросов.</param>
    /// <param name="passphrase">Passphrase аккаунта KuCoin.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public KuCoinRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, string? passphrase = null,
        IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.KuCoin(apiKey, apiSecret, passphrase ?? "") : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        const string endpoint = "/api/v1/orders";
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? "limit" : "market";

        var bodyObj = new Dictionary<string, string>
        {
            ["clientOid"] = Guid.NewGuid().ToString("N"),
            ["side"] = sideStr,
            ["symbol"] = assetId,
            ["type"] = orderType,
            ["size"] = quantity.ToString("G", CultureInfo.InvariantCulture)
        };

        if (price.HasValue)
        {
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);
            bodyObj["timeInForce"] = "GTC";
        }

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new KuCoinException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new KuCoinException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("orderId", out var orderId)
                ? MarketJsonParsingHelpers.TryGetString(orderId) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"/api/v1/orders/{Uri.EscapeDataString(orderId)}";

        var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);

        if (authenticator is null)
            throw new KuCoinException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v1/market/stats?symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

        return MarketJsonParsingHelpers.TryParseDouble(data, "last");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v1/market/orderbook/level2_20?symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

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

        return new KuCoinOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(data, "bids"),
            Asks = ParseLevels(data, "asks")
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

/// <summary>Кеш цен KuCoin.</summary>
public sealed class KuCoinPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, KuCoinPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен KuCoin без runtime-подписки.
    /// </summary>
    public KuCoinPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен KuCoin и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент KuCoin, публикующий обновления рынка.</param>
    public KuCoinPriceStream(KuCoinClient client)
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
            _ => new KuCoinPriceSnapshot
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
    /// Обновляет кеш на основании нового тикера KuCoin.
    /// </summary>
    /// <param name="symbol">Идентификатор рынка.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new KuCoinPriceSnapshot
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

[JsonSerializable(typeof(KuCoinPriceSnapshot))]
[JsonSerializable(typeof(KuCoinPosition))]
[JsonSerializable(typeof(KuCoinPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class KuCoinJsonContext : JsonSerializerContext;

#endregion
