using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Binance;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Binance Spot.
// Демонстрирует, как реализовать универсальные контракты для новой
// торговой платформы. Каждый блок помечен TODO для заполнения.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Binance.
/// </summary>
public sealed class BinancePriceSnapshot : IMarketPriceSnapshot
{
    /// <summary>Символ торговой пары (например BTCUSDT).</summary>
    public required string AssetId { get; init; }

    /// <summary>Лучшая цена покупки.</summary>
    public double? BestBid { get; set; }

    /// <summary>Лучшая цена продажи.</summary>
    public double? BestAsk { get; set; }

    /// <summary>Средняя цена.</summary>
    public double? Midpoint => (BestBid + BestAsk) / 2.0;

    /// <summary>Цена последней сделки.</summary>
    public double? LastTradePrice { get; set; }

    /// <summary>Время обновления (ticks).</summary>
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Binance.
/// </summary>
public sealed class BinancePosition : IMarketPosition
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
/// Сводка портфеля Binance.
/// </summary>
public sealed class BinancePortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Binance.
/// </summary>
public sealed class BinanceOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал Binance.
/// </summary>
public sealed class BinanceTradeSignal : IMarketTradeSignal
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

/// <summary>
/// Тип Binance stream для runtime-подписки.
/// </summary>
public enum BinanceStreamType : byte
{
    /// <summary>
    /// Поток лучшего bid/ask.
    /// </summary>
    BookTicker,

    /// <summary>
    /// Поток отдельных сделок.
    /// </summary>
    Trade,

    /// <summary>
    /// Поток агрегированных сделок.
    /// </summary>
    AggregateTrade,

    /// <summary>
    /// Поток свечей.
    /// </summary>
    Kline,

    /// <summary>
    /// Поток 24-часовой статистики тикера.
    /// </summary>
    TwentyFourHourTicker
}

/// <summary>
/// Immutable-конфигурация stream-selection для BinanceClient.
/// </summary>
/// <param name="StreamType">Тип целевого Binance stream.</param>
/// <param name="Interval">Интервал свечи для Kline stream.</param>
public sealed record BinanceStreamSelection(BinanceStreamType StreamType, string? Interval = null)
{
    /// <summary>
    /// Значение по умолчанию для обратной совместимости.
    /// </summary>
    public static BinanceStreamSelection Default { get; } = new(BinanceStreamType.BookTicker);
}

#endregion

#region Исключение

/// <summary>
/// Исключение операций Binance.
/// </summary>
public sealed class BinanceException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Binance Spot для получения рыночных данных в реальном времени.
/// </summary>
/// <remarks>
/// Подключается к wss://stream.binance.com:9443/ws
/// Поддерживает bookTicker, trade, kline и другие стримы.
/// </remarks>
public class BinanceClient : ExchangeClientBase
{
    /// <summary>Базовый URL WebSocket API.</summary>
    public const string DefaultWsUrl = "wss://stream.binance.com:9443/ws";

    private readonly ConcurrentDictionary<int, BinancePendingAckRequest> pendingAckRequests = new();
    private readonly BinanceStreamSelection streamSelection;
    private int nextRequestId;

    /// <summary>
    /// Создаёт WebSocket-клиент Binance Spot для рыночных подписок.
    /// </summary>
    /// <param name="streamSelection">Конфигурация целевого stream-типа для runtime-подписок.</param>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public BinanceClient(
        BinanceStreamSelection? streamSelection = null,
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
        this.streamSelection = streamSelection ?? BinanceStreamSelection.Default;
    }

    /// <inheritdoc />
    public override string PlatformName => "Binance";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return BuildTrackedCommandMessage("SUBSCRIBE", marketIds, BinancePendingAckRequestKind.Subscribe);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        return BuildTrackedCommandMessage("UNSUBSCRIBE", marketIds, BinancePendingAckRequestKind.Unsubscribe);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("data", out var dataProperty)
            && dataProperty.ValueKind is JsonValueKind.Object)
        {
            root = dataProperty;
        }

        if (await TryHandleAcknowledgementAsync(root).ConfigureAwait(false))
            return;

        if (root.TryGetProperty("code", out var codeProperty)
            && root.TryGetProperty("msg", out var messageProperty))
        {
            var code = MarketJsonParsingHelpers.TryGetString(codeProperty);
            var message = MarketJsonParsingHelpers.TryGetString(messageProperty) ?? "Unknown Binance runtime error.";
            await PublishRuntimeErrorAsync(new BinanceException($"Binance WebSocket error {code}: {message}")).ConfigureAwait(false);
            return;
        }

        // bookTicker stream payload: { "u": ..., "s": "BTCUSDT", "b": "...", "a": "..." }
        if (!root.TryGetProperty("s", out var symbolProperty))
            return;

        var assetId = MarketJsonParsingHelpers.TryGetString(symbolProperty);
        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var eventKind = root.TryGetProperty("e", out var eventProperty)
            ? MarketJsonParsingHelpers.TryGetString(eventProperty)
            : null;

        if (string.Equals(eventKind, "trade", StringComparison.Ordinal)
            || string.Equals(eventKind, "aggTrade", StringComparison.Ordinal))
        {
            var tradePrice = MarketJsonParsingHelpers.TryParseDouble(root, "p");
            if (!tradePrice.HasValue)
                return;

            await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
                assetId,
                null,
                null,
                tradePrice,
                Environment.TickCount64,
                MarketRealtimeUpdateKind.Trade)).ConfigureAwait(false);
            return;
        }

        if (string.Equals(eventKind, "kline", StringComparison.Ordinal)
            && root.TryGetProperty("k", out var klineProperty)
            && klineProperty.ValueKind is JsonValueKind.Object)
        {
            var closePrice = MarketJsonParsingHelpers.TryParseDouble(klineProperty, "c");
            if (!closePrice.HasValue)
                return;

            await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
                assetId,
                null,
                null,
                closePrice,
                Environment.TickCount64,
                MarketRealtimeUpdateKind.Ticker)).ConfigureAwait(false);
            return;
        }

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(root, "b");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(root, "a");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(root, "c") ?? MarketJsonParsingHelpers.TryParseDouble(root, "p");

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

    private static bool TryGetRequestId(JsonElement root, out int requestId)
    {
        requestId = default;
        var parsedRequestId = MarketJsonParsingHelpers.TryParseInt32(root, "id");
        if (!parsedRequestId.HasValue)
            return false;

        requestId = parsedRequestId.Value;
        return true;
    }

    private ReadOnlyMemory<byte> BuildTrackedCommandMessage(
        string method,
        string[] marketIds,
        BinancePendingAckRequestKind requestKind)
    {
        var streams = marketIds.Select(BuildStreamName).ToArray();
        var requestId = Interlocked.Increment(ref nextRequestId);
        pendingAckRequests[requestId] = new BinancePendingAckRequest(requestKind, [.. marketIds]);
        return BuildCommandMessage(method, streams, requestId);
    }

    private async ValueTask<bool> TryHandleAcknowledgementAsync(JsonElement root)
    {
        // ACK/response: { "result": null, "id": 1 }
        if (!root.TryGetProperty("result", out var resultProperty)
            || resultProperty.ValueKind is not JsonValueKind.Null
            || !TryGetRequestId(root, out var requestId))
        {
            return false;
        }

        if (pendingAckRequests.TryRemove(requestId, out var request)
            && request.Kind is BinancePendingAckRequestKind.Subscribe)
        {
            await PublishSubscriptionAcknowledgedAsync(request.MarketIds, isResubscription: false).ConfigureAwait(false);
        }

        return true;
    }

    private string BuildStreamName(string marketId)
    {
        var normalizedMarketId = marketId.ToLowerInvariant();
        return streamSelection.StreamType switch
        {
            BinanceStreamType.BookTicker => $"{normalizedMarketId}@bookTicker",
            BinanceStreamType.Trade => $"{normalizedMarketId}@trade",
            BinanceStreamType.AggregateTrade => $"{normalizedMarketId}@aggTrade",
            BinanceStreamType.Kline => $"{normalizedMarketId}@kline_{streamSelection.Interval ?? "1m"}",
            BinanceStreamType.TwentyFourHourTicker => $"{normalizedMarketId}@ticker",
            _ => $"{normalizedMarketId}@bookTicker"
        };
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string method, string[] streams, int requestId)
    {
        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append("{\"method\":\"");
        builder.Append(method);
        builder.Append("\",\"params\":[");

        for (var index = 0; index < streams.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append(streams[index]);
            builder.Append('"');
        }

        builder.Append("],\"id\":");
        builder.Append(requestId.ToString(CultureInfo.InvariantCulture));
        builder.Append('}');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private enum BinancePendingAckRequestKind : byte
    {
        Subscribe,
        Unsubscribe
    }

    private readonly record struct BinancePendingAckRequest(BinancePendingAckRequestKind Kind, string[] MarketIds);
}

#endregion

#region IMarketRestClient — REST API

/// <summary>
/// REST-клиент Binance Spot API (v3).
/// </summary>
/// <remarks>
/// Поддерживает публичные и подписанные (HMAC-SHA256) эндпоинты.
/// Для приватных операций (ордера) требуется apiKey + apiSecret.
/// </remarks>
public sealed class BinanceRestClient : IMarketRestClient, IDisposable
{
    /// <summary>Базовый URL Binance Spot API.</summary>
    public const string DefaultApiUrl = "https://api.binance.com";

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
    /// Создаёт REST-клиент Binance.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ для подписанных запросов.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public BinanceRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;

        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Binance(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v3/order
        if (authenticator is null)
            throw new BinanceException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var query = $"symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}" +
                    $"&side={sideStr}" +
                    $"&type={orderType}" +
                    $"&quantity={qtyStr}" +
                    $"&timestamp={ts}";

        if (price.HasValue)
        {
            var priceStr = price.Value.ToString("G", CultureInfo.InvariantCulture);
            query += $"&timeInForce=GTC&price={priceStr}";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v3/order?{query}");
        authenticator.SignRequest(request, query);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BinanceException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("orderId", out var orderId)
            ? orderId.GetInt64().ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // DELETE /api/v3/order — требуется symbol, но orderId глобально уникален в Binance.
        // Допущение: orderId передаётся в формате "SYMBOL:ORDER_ID" (напр. "BTCUSDT:12345").
        if (authenticator is null)
            throw new BinanceException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var parts = orderId.Split(':', 2);
        var (symbol, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new BinanceException("orderId должен быть в формате 'SYMBOL:ORDER_ID'.");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var query = $"symbol={Uri.EscapeDataString(symbol)}" +
                    $"&orderId={Uri.EscapeDataString(oid)}" +
                    $"&timestamp={ts}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{query}");
        authenticator.SignRequest(request, query);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/ticker/price?symbol=BTCUSDT
        var response = await httpClient.GetAsync(
            $"/api/v3/ticker/price?symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("price", out var priceEl)
            ? MarketJsonParsingHelpers.TryParseDouble(priceEl)
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/depth?symbol=BTCUSDT&limit=20
        var response = await httpClient.GetAsync(
            $"/api/v3/depth?symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}&limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        static (double Price, double Qty)[] ParseLevels(JsonElement arr)
        {
            var result = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < result.Length; i++)
            {
                var level = arr[i];
                result[i] = (
                    MarketJsonParsingHelpers.TryParseDouble(level[0]) ?? 0,
                    MarketJsonParsingHelpers.TryParseDouble(level[1]) ?? 0);
            }
            return result;
        }

        return new BinanceOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = doc.RootElement.TryGetProperty("bids", out var bids) ? ParseLevels(bids) : [],
            Asks = doc.RootElement.TryGetProperty("asks", out var asks) ? ParseLevels(asks) : []
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
/// Кеш цен Binance с автоматическим обновлением из WebSocket.
/// </summary>
public sealed class BinancePriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BinancePriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Binance без runtime-подписки.
    /// </summary>
    public BinancePriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Binance и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Binance, публикующий обновления рынка.</param>
    public BinancePriceStream(BinanceClient client)
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
            _ => new BinancePriceSnapshot
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
        SetPrice(symbol, new BinancePriceSnapshot
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

/// <summary>
/// Source-generated JSON контекст для NativeAOT-совместимости.
/// </summary>
[JsonSerializable(typeof(BinancePriceSnapshot))]
[JsonSerializable(typeof(BinancePosition))]
[JsonSerializable(typeof(BinancePosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BinanceJsonContext : JsonSerializerContext;

#endregion
