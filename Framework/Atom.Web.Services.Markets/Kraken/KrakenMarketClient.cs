using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Kraken;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Kraken Spot.
// WebSocket API v2 + REST API.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Kraken.
/// </summary>
public sealed class KrakenPriceSnapshot : IMarketPriceSnapshot
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
/// Позиция на Kraken.
/// </summary>
public sealed class KrakenPosition : IMarketPosition
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
/// Сводка портфеля Kraken.
/// </summary>
public sealed class KrakenPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Kraken.
/// </summary>
public sealed class KrakenOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал Kraken.
/// </summary>
public sealed class KrakenTradeSignal : IMarketTradeSignal
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
/// Исключение операций Kraken.
/// </summary>
public sealed class KrakenException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v2

/// <summary>
/// WebSocket-клиент Kraken v2 для получения рыночных данных.
/// </summary>
/// <remarks>
/// Подключается к wss://ws.kraken.com/v2
/// Поддерживает book, ticker, trade, ohlc каналы.
/// </remarks>
public class KrakenClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый адрес WebSocket API Kraken.
    /// </summary>
    public const string DefaultWsUrl = "wss://ws.kraken.com/v2";

    /// <summary>
    /// Создаёт WebSocket-клиент Kraken.
    /// </summary>
    /// <param name="reconnectDelay">Задержка между попытками переподключения.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. 0 означает без ограничения.</param>
    /// <param name="pingInterval">Интервал отправки ping-сообщений runtime.</param>
    public KrakenClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Kraken";

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

        if (root.TryGetProperty("method", out var methodProperty))
        {
            var method = MarketJsonParsingHelpers.TryGetString(methodProperty);
            if ((string.Equals(method, "subscribe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(method, "unsubscribe", StringComparison.OrdinalIgnoreCase))
                && root.TryGetProperty("success", out var successProperty)
                && successProperty.ValueKind is JsonValueKind.True)
            {
                var marketIds = ExtractAcknowledgedMarketIds(root);
                if (marketIds.Length > 0 && string.Equals(method, "subscribe", StringComparison.OrdinalIgnoreCase))
                    await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

                return;
            }

            if (root.TryGetProperty("success", out successProperty)
                && successProperty.ValueKind is JsonValueKind.False)
            {
                var error = MarketJsonParsingHelpers.TryGetString(root, "error")
                    ?? MarketJsonParsingHelpers.TryGetString(root, "message")
                    ?? method
                    ?? "error";
                await PublishRuntimeErrorAsync(new KrakenException($"Kraken WebSocket {method}: {error}".Trim())).ConfigureAwait(false);
                return;
            }
        }

        if (!MarketJsonParsingHelpers.PropertyEquals(root, "channel", "ticker")
            || !root.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var tickerElement in dataProperty.EnumerateArray())
        {
            var assetId = MarketJsonParsingHelpers.TryGetString(tickerElement, "symbol");
            if (string.IsNullOrWhiteSpace(assetId))
                continue;

            var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "bid");
            var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "ask");
            var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "last");

            if (!bestBid.HasValue && !bestAsk.HasValue && !lastTrade.HasValue)
                continue;

            await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
                assetId,
                bestBid,
                bestAsk,
                lastTrade,
                Environment.TickCount64,
                MarketRealtimeUpdateKind.Ticker)).ConfigureAwait(false);
        }
    }

    private static string[] ExtractAcknowledgedMarketIds(JsonElement root)
    {
        if (root.TryGetProperty("result", out var resultProperty))
        {
            if (resultProperty.ValueKind is JsonValueKind.Object)
            {
                if (resultProperty.TryGetProperty("symbol", out var symbolProperty))
                    return ExtractSymbols(symbolProperty);
            }
            else if (resultProperty.ValueKind is JsonValueKind.Array)
            {
                return resultProperty.EnumerateArray()
                    .Select(MarketJsonParsingHelpers.TryGetString)
                    .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                    .ToArray()!;
            }
        }

        if (root.TryGetProperty("params", out var paramsProperty)
            && paramsProperty.ValueKind is JsonValueKind.Object
            && paramsProperty.TryGetProperty("symbol", out var paramsSymbolProperty))
        {
            return ExtractSymbols(paramsSymbolProperty);
        }

        return [];
    }

    private static string[] ExtractSymbols(JsonElement symbolProperty)
    {
        if (symbolProperty.ValueKind is JsonValueKind.Array)
        {
            return symbolProperty.EnumerateArray()
                .Select(MarketJsonParsingHelpers.TryGetString)
                .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                .ToArray()!;
        }

        var symbol = MarketJsonParsingHelpers.TryGetString(symbolProperty);
        return string.IsNullOrWhiteSpace(symbol) ? [] : [symbol];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string method, string[] marketIds)
    {
        var builder = new StringBuilder();
        builder.Append("{\"method\":\"");
        builder.Append(method);
        builder.Append("\",\"params\":{\"channel\":\"ticker\",\"symbol\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append(marketIds[index]);
            builder.Append('"');
        }

        builder.Append("]}}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API

/// <summary>
/// REST-клиент Kraken API (v0).
/// </summary>
/// <remarks>
/// Публичные: /0/public/Ticker, /0/public/Depth
/// Приватные: /0/private/AddOrder — HMAC-SHA512 + nonce подпись.
/// </remarks>
public sealed class KrakenRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Базовый адрес REST API Kraken.
    /// </summary>
    public const string DefaultApiUrl = "https://api.kraken.com";

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
    /// Создаёт REST-клиент Kraken.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ для подписанных запросов.</param>
    /// <param name="apiSecret">Секрет (Base64) для HMAC-SHA512 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public KrakenRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;

        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Kraken(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /0/private/AddOrder
        if (authenticator is null)
            throw new KrakenException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        const string urlPath = "/0/private/AddOrder";
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? "limit" : "market";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);

        var postData = $"pair={Uri.EscapeDataString(assetId)}" +
                       $"&type={sideStr}&ordertype={orderType}&volume={qtyStr}";

        if (price.HasValue)
            postData += $"&price={price.Value.ToString("G", CultureInfo.InvariantCulture)}";

        var request = new HttpRequestMessage(HttpMethod.Post, urlPath);
        authenticator.SignRequest(request, postData);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new KrakenException($"AddOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("txid", out var txids)
            && txids.GetArrayLength() > 0)
            return txids[0].GetString();

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /0/private/CancelOrder
        if (authenticator is null)
            throw new KrakenException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        const string urlPath = "/0/private/CancelOrder";
        var postData = $"txid={Uri.EscapeDataString(orderId)}";

        var request = new HttpRequestMessage(HttpMethod.Post, urlPath);
        authenticator.SignRequest(request, postData);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /0/public/Ticker?pair=XBTUSD
        var response = await httpClient.GetAsync(
            $"/0/public/Ticker?pair={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

        foreach (var pair in result.EnumerateObject())
        {
            if (!pair.Value.TryGetProperty("c", out var closes)) continue;
            var parsedPrice = MarketJsonParsingHelpers.TryParseDouble(closes[0]);
            if (parsedPrice.HasValue)
                return parsedPrice.Value;
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /0/public/Depth?pair=XBTUSD&count=20
        var response = await httpClient.GetAsync(
            $"/0/public/Depth?pair={Uri.EscapeDataString(assetId)}&count=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

        static (double Price, double Qty)[] ParseLevels(JsonElement arr)
        {
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

        foreach (var pair in result.EnumerateObject())
        {
            return new KrakenOrderBookSnapshot
            {
                AssetId = assetId,
                Timestamp = DateTimeOffset.UtcNow,
                Bids = pair.Value.TryGetProperty("bids", out var bids) ? ParseLevels(bids) : [],
                Asks = pair.Value.TryGetProperty("asks", out var asks) ? ParseLevels(asks) : []
            };
        }

        return null;
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
/// Кеш цен Kraken.
/// </summary>
public sealed class KrakenPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, KrakenPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт пустой поток цен Kraken.
    /// </summary>
    public KrakenPriceStream()
    {
    }

    /// <summary>
    /// Создаёт поток цен Kraken и связывает его с runtime-клиентом.
    /// </summary>
    /// <param name="client">Клиент, публикующий рыночные обновления.</param>
    public KrakenPriceStream(KrakenClient client)
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
            _ => new KrakenPriceSnapshot
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
        SetPrice(pair, new KrakenPriceSnapshot
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

[JsonSerializable(typeof(KrakenPriceSnapshot))]
[JsonSerializable(typeof(KrakenPosition))]
[JsonSerializable(typeof(KrakenPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class KrakenJsonContext : JsonSerializerContext;

#endregion
