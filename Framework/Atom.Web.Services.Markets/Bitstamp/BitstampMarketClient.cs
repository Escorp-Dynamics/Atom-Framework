using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitstamp;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для Bitstamp API v2.
// WebSocket (wss://ws.bitstamp.net) + REST API v2.
// HMAC-SHA256 подпись ордеров.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены Bitstamp.</summary>
public sealed class BitstampPriceSnapshot : IMarketPriceSnapshot
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

/// <summary>Позиция на Bitstamp.</summary>
public sealed class BitstampPosition : IMarketPosition
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

/// <summary>Сводка портфеля Bitstamp.</summary>
public sealed class BitstampPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров Bitstamp.</summary>
public sealed class BitstampOrderBookSnapshot : IMarketOrderBookSnapshot
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

/// <summary>Торговый сигнал Bitstamp.</summary>
public sealed class BitstampTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций Bitstamp.</summary>
public sealed class BitstampException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Bitstamp для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://ws.bitstamp.net — JSON push.
/// Подписка: {"event":"bts:subscribe","data":{"channel":"live_trades_btcusd"}}.
/// </remarks>
public class BitstampClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Bitstamp.
    /// </summary>
    public const string DefaultWsUrl = "wss://ws.bitstamp.net";

    /// <summary>
    /// Создаёт WebSocket-клиент Bitstamp для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public BitstampClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Bitstamp";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId) ? ReadOnlyMemory<byte>.Empty : BuildCommandMessage("bts:subscribe", marketId);
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            if (string.IsNullOrWhiteSpace(marketId))
                continue;

            yield return BuildCommandMessage("bts:subscribe", marketId);
        }
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId) ? ReadOnlyMemory<byte>.Empty : BuildCommandMessage("bts:unsubscribe", marketId);
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            if (string.IsNullOrWhiteSpace(marketId))
                continue;

            yield return BuildCommandMessage("bts:unsubscribe", marketId);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var eventName = MarketJsonParsingHelpers.TryGetString(root, "event");
        if (string.Equals(eventName, "bts:subscription_succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var marketIds = ExtractAcknowledgedMarketIds(root);
            if (marketIds.Length > 0)
                await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

            return;
        }

        if (string.Equals(eventName, "bts:error", StringComparison.OrdinalIgnoreCase))
        {
            var errorMessage = MarketJsonParsingHelpers.TryGetString(root, "message")
                ?? (root.TryGetProperty("data", out var dataProperty)
                    ? MarketJsonParsingHelpers.TryGetString(dataProperty, "message")
                    : null)
                ?? "Unknown Bitstamp runtime error.";

            await PublishRuntimeErrorAsync(new BitstampException(errorMessage)).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(eventName, "trade", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("channel", out var channelProperty)
            || !root.TryGetProperty("data", out var tradeProperty)
            || tradeProperty.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var assetId = ExtractMarketId(channelProperty);
        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tradeProperty, "price");
        if (!lastTrade.HasValue)
            return;

        await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
            assetId,
            null,
            null,
            lastTrade,
            Environment.TickCount64,
            MarketRealtimeUpdateKind.Trade)).ConfigureAwait(false);
    }

    private static string[] ExtractAcknowledgedMarketIds(JsonElement root)
    {
        if (!root.TryGetProperty("channel", out var channelProperty))
            return [];

        var marketId = ExtractMarketId(channelProperty);
        return string.IsNullOrWhiteSpace(marketId) ? [] : [marketId];
    }

    private static string? ExtractMarketId(JsonElement channelProperty)
    {
        var channel = MarketJsonParsingHelpers.TryGetString(channelProperty);
        if (string.IsNullOrWhiteSpace(channel)
            || !channel.StartsWith("live_trades_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return channel["live_trades_".Length..];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string @event, string marketId)
    {
        var builder = new StringBuilder();
        builder.Append("{\"event\":\"");
        builder.Append(@event);
        builder.Append("\",\"data\":{\"channel\":\"live_trades_");
        builder.Append(marketId);
        builder.Append("\"}}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API v2 + HMAC-SHA256

/// <summary>
/// REST-клиент Bitstamp API v2.
/// </summary>
/// <remarks>
/// Bitstamp v2: HMAC-SHA256 подпись: X-Auth = "BITSTAMP {apiKey}", HMAC(timestamp+nonce+content-type+path+query+body).
/// POST /api/v2/buy/market/btcusd/, POST /api/v2/sell/limit/btcusd/
/// GET /api/v2/ticker/btcusd/, GET /api/v2/order_book/btcusd/
/// </remarks>
public sealed class BitstampRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API Bitstamp v2.
    /// </summary>
    public const string DefaultApiUrl = "https://www.bitstamp.net";

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
    /// Создаёт REST-клиент Bitstamp.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, который будет использован для запросов.</param>
    /// <param name="apiKey">API-ключ биржи.</param>
    /// <param name="apiSecret">Секрет API для подписи запросов.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public BitstampRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bitstamp(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new BitstampException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? "limit" : "market";
        var path = $"/api/v2/{sideStr}/{orderType}/{assetId}/";

        var formData = new Dictionary<string, string>
        {
            ["amount"] = quantity.ToString("G", CultureInfo.InvariantCulture)
        };

        if (price.HasValue)
            formData["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var content = new FormUrlEncodedContent(formData);
        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        authenticator.SignRequest(request, body);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BitstampException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("id", out var id) ? MarketJsonParsingHelpers.TryGetString(id) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new BitstampException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        const string path = "/api/v2/cancel_order/";
        var formData = new Dictionary<string, string> { ["id"] = orderId };
        var content = new FormUrlEncodedContent(formData);
        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        authenticator.SignRequest(request, body);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v2/ticker/{Uri.EscapeDataString(assetId)}/",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return MarketJsonParsingHelpers.TryParseDouble(doc.RootElement, "last");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v2/order_book/{Uri.EscapeDataString(assetId)}/",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        static (double Price, double Qty)[] ParseLevels(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var arr)) return [];
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

        return new BitstampOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(doc.RootElement, "bids"),
            Asks = ParseLevels(doc.RootElement, "asks")
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

/// <summary>Кеш цен Bitstamp.</summary>
public sealed class BitstampPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BitstampPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Bitstamp без runtime-подписки.
    /// </summary>
    public BitstampPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Bitstamp и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Bitstamp, публикующий обновления рынка.</param>
    public BitstampPriceStream(BitstampClient client)
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
            _ => new BitstampPriceSnapshot
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
    /// Обновляет кеш на основании нового trade update от Bitstamp.
    /// </summary>
    /// <param name="symbol">Идентификатор рынка.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new BitstampPriceSnapshot
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

[JsonSerializable(typeof(BitstampPriceSnapshot))]
[JsonSerializable(typeof(BitstampPosition))]
[JsonSerializable(typeof(BitstampPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BitstampJsonContext : JsonSerializerContext;

#endregion
