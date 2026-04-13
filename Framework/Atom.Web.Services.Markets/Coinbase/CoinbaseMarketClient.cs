using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Coinbase;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Coinbase Advanced Trade API.
// WebSocket (wss://advanced-trade-ws.coinbase.com) + REST v3.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Coinbase.
/// </summary>
public sealed class CoinbasePriceSnapshot : IMarketPriceSnapshot
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
/// Позиция на Coinbase.
/// </summary>
public sealed class CoinbasePosition : IMarketPosition
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
/// Сводка портфеля Coinbase.
/// </summary>
public sealed class CoinbasePortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Coinbase.
/// </summary>
public sealed class CoinbaseOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал Coinbase.
/// </summary>
public sealed class CoinbaseTradeSignal : IMarketTradeSignal
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
/// Исключение операций Coinbase.
/// </summary>
public sealed class CoinbaseException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket (Advanced Trade)

/// <summary>
/// WebSocket-клиент Coinbase Advanced Trade для получения рыночных данных.
/// </summary>
/// <remarks>
/// Подключается к wss://advanced-trade-ws.coinbase.com
/// Каналы: ticker, level2, market_trades, candles.
/// Требуется JWT-токен для аутентифицированных каналов.
/// </remarks>
public class CoinbaseClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Coinbase Advanced Trade.
    /// </summary>
    public const string DefaultWsUrl = "wss://advanced-trade-ws.coinbase.com";

    /// <summary>
    /// Создаёт WebSocket-клиент Coinbase Advanced Trade для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public CoinbaseClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Coinbase";

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

        if (root.TryGetProperty("type", out var typeProperty))
        {
            var type = MarketJsonParsingHelpers.TryGetString(typeProperty);
            if (string.Equals(type, "subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                var marketIds = ExtractAcknowledgedMarketIds(root);
                if (marketIds.Length > 0)
                    await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

                return;
            }

            if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            {
                var message = MarketJsonParsingHelpers.TryGetString(root, "message")
                    ?? MarketJsonParsingHelpers.TryGetString(root, "reason")
                    ?? "Unknown Coinbase runtime error.";

                await PublishRuntimeErrorAsync(new CoinbaseException(message ?? "Unknown Coinbase runtime error.")).ConfigureAwait(false);
                return;
            }
        }

        if (!root.TryGetProperty("channel", out var channelProperty)
            || !MarketJsonParsingHelpers.PropertyEquals(root, "channel", "ticker")
            || !root.TryGetProperty("events", out var eventsProperty)
            || eventsProperty.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var eventElement in eventsProperty.EnumerateArray())
        {
            if (!eventElement.TryGetProperty("tickers", out var tickersProperty)
                || tickersProperty.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var tickerElement in tickersProperty.EnumerateArray())
            {
                if (!tickerElement.TryGetProperty("product_id", out var productIdProperty))
                    continue;

                var assetId = MarketJsonParsingHelpers.TryGetString(productIdProperty);
                if (string.IsNullOrWhiteSpace(assetId))
                    continue;

                var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "best_bid");
                var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "best_ask");
                var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "price");

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
    }

    private static string[] ExtractAcknowledgedMarketIds(JsonElement root)
    {
        if (!root.TryGetProperty("channels", out var channelsProperty)
            || channelsProperty.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var marketIds = new List<string>();
        foreach (var channel in channelsProperty.EnumerateArray())
        {
            if (!channel.TryGetProperty("name", out var nameProperty)
                || !string.Equals(MarketJsonParsingHelpers.TryGetString(nameProperty), "ticker", StringComparison.OrdinalIgnoreCase)
                || !channel.TryGetProperty("product_ids", out var marketIdsProperty)
                || marketIdsProperty.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var marketId in marketIdsProperty.EnumerateArray())
            {
                var text = MarketJsonParsingHelpers.TryGetString(marketId);
                if (!string.IsNullOrWhiteSpace(text))
                    marketIds.Add(text);
            }
        }

        return [.. marketIds.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string type, string[] marketIds)
    {
        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append("{\"type\":\"");
        builder.Append(type);
        builder.Append("\",\"product_ids\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append(marketIds[index]);
            builder.Append('"');
        }

        builder.Append("],\"channel\":\"ticker\"}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API v3 (Advanced Trade)

/// <summary>
/// REST-клиент Coinbase Advanced Trade API v3.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v3/brokerage/market/products/{product_id}/ticker,
///            GET /api/v3/brokerage/market/products/{product_id}/book
/// Приватные: POST /api/v3/brokerage/orders — JWT подпись.
/// </remarks>
public sealed class CoinbaseRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API Coinbase Advanced Trade.
    /// </summary>
    public const string DefaultApiUrl = "https://api.coinbase.com";

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
    /// Создаёт REST-клиент Coinbase Advanced Trade.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ (Cloud API key).</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public CoinbaseRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Coinbase(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v3/brokerage/orders
        const string path = "/api/v3/brokerage/orders";
        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);

        var bodyObj = new Dictionary<string, object>
        {
            ["client_order_id"] = Guid.NewGuid().ToString("N"),
            ["product_id"] = assetId,
            ["side"] = sideStr
        };

        if (price.HasValue)
        {
            bodyObj["order_configuration"] = new Dictionary<string, object>
            {
                ["limit_limit_gtc"] = new Dictionary<string, string>
                {
                    ["base_size"] = qtyStr,
                    ["limit_price"] = price.Value.ToString("G", CultureInfo.InvariantCulture)
                }
            };
        }
        else
        {
            bodyObj["order_configuration"] = new Dictionary<string, object>
            {
                ["market_market_ioc"] = new Dictionary<string, string>
                {
                    ["base_size"] = qtyStr
                }
            };
        }

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new CoinbaseException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new CoinbaseException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("order_id", out var orderId)
            ? MarketJsonParsingHelpers.TryGetString(orderId) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /api/v3/brokerage/orders/batch_cancel
        const string path = "/api/v3/brokerage/orders/batch_cancel";
        var bodyObj = new Dictionary<string, object> { ["order_ids"] = new[] { orderId } };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new CoinbaseException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/brokerage/market/products/{product_id}/ticker
        var response = await httpClient.GetAsync(
            $"/api/v3/brokerage/market/products/{Uri.EscapeDataString(assetId)}/ticker",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("trades", out var trades)) return null;
        if (trades.GetArrayLength() == 0) return null;

        return MarketJsonParsingHelpers.TryParseDouble(trades[0], "price");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/brokerage/market/products/{product_id}/book?limit=20
        var response = await httpClient.GetAsync(
            $"/api/v3/brokerage/market/products/{Uri.EscapeDataString(assetId)}/book?limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static (double Price, double Qty)[] ParseLevels(JsonElement prop, string name)
        {
            if (!prop.TryGetProperty(name, out var arr)) return [];
            var list = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < list.Length; i++)
            {
                var level = arr[i];
                list[i] = (
                    MarketJsonParsingHelpers.TryParseDouble(level, "price") ?? 0,
                    MarketJsonParsingHelpers.TryParseDouble(level, "size") ?? 0);
            }
            return list;
        }

        if (!root.TryGetProperty("pricebook", out var book)) return null;

        return new CoinbaseOrderBookSnapshot
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

/// <summary>
/// Кеш цен Coinbase.
/// </summary>
public sealed class CoinbasePriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, CoinbasePriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Coinbase без runtime-подписки.
    /// </summary>
    public CoinbasePriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Coinbase и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Coinbase, публикующий обновления рынка.</param>
    public CoinbasePriceStream(CoinbaseClient client)
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
            _ => new CoinbasePriceSnapshot
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
    public void UpdatePrice(string productId, double bid, double ask, double lastTrade)
    {
        SetPrice(productId, new CoinbasePriceSnapshot
        {
            AssetId = productId,
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

[JsonSerializable(typeof(CoinbasePriceSnapshot))]
[JsonSerializable(typeof(CoinbasePosition))]
[JsonSerializable(typeof(CoinbasePosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CoinbaseJsonContext : JsonSerializerContext;

#endregion
