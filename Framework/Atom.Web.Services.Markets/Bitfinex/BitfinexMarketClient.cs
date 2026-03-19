using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitfinex;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Bitfinex API v2.
// WebSocket (wss://api-pub.bitfinex.com/ws/2) + REST v2.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Bitfinex.
/// </summary>
public sealed class BitfinexPriceSnapshot : IMarketPriceSnapshot
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
/// Позиция на Bitfinex.
/// </summary>
public sealed class BitfinexPosition : IMarketPosition
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
/// Сводка портфеля Bitfinex.
/// </summary>
public sealed class BitfinexPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Bitfinex.
/// </summary>
public sealed class BitfinexOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал Bitfinex.
/// </summary>
public sealed class BitfinexTradeSignal : IMarketTradeSignal
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
/// Исключение операций Bitfinex.
/// </summary>
public sealed class BitfinexException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v2

/// <summary>
/// WebSocket-клиент Bitfinex v2.
/// </summary>
/// <remarks>
/// Подключается к wss://api-pub.bitfinex.com/ws/2
/// Каналы: ticker, trades, book. Данные в массивном формате (не JSON-объекты).
/// </remarks>
public class BitfinexClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Bitfinex v2.
    /// </summary>
    public const string DefaultWsUrl = "wss://api-pub.bitfinex.com/ws/2";

    private readonly ConcurrentDictionary<int, string> channelMarketIds = new();
    private readonly ConcurrentDictionary<string, int> marketChannelIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Создаёт WebSocket-клиент Bitfinex v2 для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public BitfinexClient(
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
    public override string PlatformName => "Bitfinex";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return marketIds.Length == 0
            ? ReadOnlyMemory<byte>.Empty
            : BuildSubscribePayload(marketIds[0]);
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
            yield return BuildSubscribePayload(marketId);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        if (marketIds.Length == 0)
            return ReadOnlyMemory<byte>.Empty;

        return marketChannelIds.TryGetValue(marketIds[0], out var channelId)
            ? BuildUnsubscribePayload(channelId)
            : ReadOnlyMemory<byte>.Empty;
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            if (marketChannelIds.TryGetValue(marketId, out var channelId))
                yield return BuildUnsubscribePayload(channelId);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            await HandleEventMessageAsync(root).ConfigureAwait(false);
            return;
        }

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            return;

        var channelId = MarketJsonParsingHelpers.TryParseInt32(root[0]);
        if (!channelId.HasValue || !channelMarketIds.TryGetValue(channelId.Value, out var assetId))
            return;

        var updateProperty = root[1];
        if (updateProperty.ValueKind == JsonValueKind.String
            && string.Equals(updateProperty.GetString(), "hb", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (updateProperty.ValueKind != JsonValueKind.Array)
            return;

        var indices = GetTickerIndices(assetId, updateProperty.GetArrayLength());
        if (!indices.HasValue)
            return;

        var (bidIndex, askIndex, lastIndex) = indices.Value;
        var bestBid = MarketJsonParsingHelpers.TryParseDouble(updateProperty[bidIndex]);
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(updateProperty[askIndex]);
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(updateProperty[lastIndex]);

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

    /// <inheritdoc />
    protected override ValueTask OnDisconnectedAsync(Exception? exception, CancellationToken cancellationToken)
    {
        channelMarketIds.Clear();
        marketChannelIds.Clear();
        return ValueTask.CompletedTask;
    }

    private async ValueTask HandleEventMessageAsync(JsonElement root)
    {
        var eventName = MarketJsonParsingHelpers.TryGetString(root, "event");
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        if (string.Equals(eventName, "subscribed", StringComparison.OrdinalIgnoreCase))
        {
            if (!MarketJsonParsingHelpers.PropertyEquals(root, "channel", "ticker"))
                return;

            var channelId = MarketJsonParsingHelpers.TryParseInt32(root, "chanId");
            var marketId = MarketJsonParsingHelpers.TryGetString(root, "symbol");
            if (!channelId.HasValue || string.IsNullOrWhiteSpace(marketId))
                return;

            channelMarketIds[channelId.Value] = marketId;
            marketChannelIds[marketId] = channelId.Value;
            await PublishSubscriptionAcknowledgedAsync([marketId], isResubscription: false).ConfigureAwait(false);
            return;
        }

        if (string.Equals(eventName, "unsubscribed", StringComparison.OrdinalIgnoreCase))
        {
            var channelId = MarketJsonParsingHelpers.TryParseInt32(root, "chanId");
            if (!channelId.HasValue)
                return;

            if (channelMarketIds.TryRemove(channelId.Value, out var marketId))
                marketChannelIds.TryRemove(marketId, out _);

            return;
        }

        if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
        {
            var code = MarketJsonParsingHelpers.TryGetString(root, "code") ?? "unknown";
            var message = MarketJsonParsingHelpers.TryGetString(root, "msg")
                ?? MarketJsonParsingHelpers.TryGetString(root, "message")
                ?? "Unknown Bitfinex runtime error.";
            await PublishRuntimeErrorAsync(new BitfinexException($"Bitfinex WebSocket error {code}: {message}"))
                .ConfigureAwait(false);
        }
    }

    private static ReadOnlyMemory<byte> BuildSubscribePayload(string marketId)
    {
        return Encoding.UTF8.GetBytes($"{{\"event\":\"subscribe\",\"channel\":\"ticker\",\"symbol\":\"{marketId}\"}}");
    }

    private static ReadOnlyMemory<byte> BuildUnsubscribePayload(int channelId)
    {
        return Encoding.UTF8.GetBytes($"{{\"event\":\"unsubscribe\",\"chanId\":{channelId}}}");
    }

    private static (int BidIndex, int AskIndex, int LastIndex)? GetTickerIndices(string assetId, int length)
    {
        if (assetId.StartsWith('f') || assetId.StartsWith('F'))
            return length > 9 ? (1, 4, 9) : null;

        return length > 6 ? (0, 2, 6) : null;
    }
}

#endregion

#region IMarketRestClient — REST API v2

/// <summary>
/// REST-клиент Bitfinex API v2.
/// </summary>
/// <remarks>
/// Публичные: GET /v2/ticker/{Symbol}, GET /v2/book/{Symbol}/P0
/// Приватные: POST /v2/auth/... — HMAC-SHA384 + nonce подпись.
/// </remarks>
public sealed class BitfinexRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API Bitfinex v2.
    /// </summary>
    public const string DefaultApiUrl = "https://api-pub.bitfinex.com";

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
    /// Создаёт REST-клиент Bitfinex v2.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA384 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public BitfinexRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bitfinex(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /v2/auth/w/order/submit
        const string path = "/v2/auth/w/order/submit";
        var amount = side == TradeSide.Buy
            ? quantity.ToString("G", CultureInfo.InvariantCulture)
            : $"-{quantity.ToString("G", CultureInfo.InvariantCulture)}";
        var orderType = price.HasValue ? "EXCHANGE LIMIT" : "EXCHANGE MARKET";

        var bodyObj = new Dictionary<string, object>
        {
            ["type"] = orderType,
            ["symbol"] = assetId,
            ["amount"] = amount
        };

        if (price.HasValue)
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new BitfinexException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BitfinexException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        // Ответ Bitfinex v2: [MTS, TYPE, MSG_ID, null, [ORDER_ID, ...]]
        var arr = doc.RootElement;
        if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 4)
        {
            var orderArr = arr[4];
            if (orderArr.ValueKind == JsonValueKind.Array && orderArr.GetArrayLength() > 0)
                return orderArr[0].GetInt64().ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /v2/auth/w/order/cancel
        const string path = "/v2/auth/w/order/cancel";

        if (!long.TryParse(orderId, out var id))
            throw new BitfinexException($"orderId должен быть числовым: '{orderId}'.");

        var bodyObj = new Dictionary<string, long> { ["id"] = id };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new BitfinexException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /v2/ticker/{Symbol} — возвращает массив [BID, BID_SIZE, ASK, ASK_SIZE, ...]
        var response = await httpClient.GetAsync(
            $"/v2/ticker/{Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        // [0] = BID, [2] = ASK, [6] = LAST_PRICE
        return arr.GetArrayLength() > 6
            ? arr[6].GetDouble()
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /v2/book/{Symbol}/P0 — [[PRICE, COUNT, AMOUNT], ...]
        var response = await httpClient.GetAsync(
            $"/v2/book/{Uri.EscapeDataString(assetId)}/P0",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        var bids = new List<(double, double)>();
        var asks = new List<(double, double)>();

        for (int i = 0; i < arr.GetArrayLength(); i++)
        {
            var level = arr[i];
            var p = level[0].GetDouble();
            var amount = level[2].GetDouble();

            if (amount > 0)
                bids.Add((p, amount));
            else
                asks.Add((p, Math.Abs(amount)));
        }

        return new BitfinexOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = bids.ToArray(),
            Asks = asks.ToArray()
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
/// Кеш цен Bitfinex.
/// </summary>
public sealed class BitfinexPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BitfinexPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Bitfinex без runtime-подписки.
    /// </summary>
    public BitfinexPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Bitfinex и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Bitfinex, публикующий обновления рынка.</param>
    public BitfinexPriceStream(BitfinexClient client)
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
            _ => new BitfinexPriceSnapshot
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
        SetPrice(symbol, new BitfinexPriceSnapshot
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

[JsonSerializable(typeof(BitfinexPriceSnapshot))]
[JsonSerializable(typeof(BitfinexPosition))]
[JsonSerializable(typeof(BitfinexPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BitfinexJsonContext : JsonSerializerContext;

#endregion
