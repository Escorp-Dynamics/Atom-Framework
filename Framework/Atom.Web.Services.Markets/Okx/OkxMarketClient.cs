using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Okx;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для OKX API v5.
// WebSocket (wss://ws.okx.com:8443/ws/v5/public) + REST v5.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива OKX.
/// </summary>
public sealed class OkxPriceSnapshot : IMarketPriceSnapshot
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
/// Позиция на OKX.
/// </summary>
public sealed class OkxPosition : IMarketPosition
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
/// Сводка портфеля OKX.
/// </summary>
public sealed class OkxPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров OKX.
/// </summary>
public sealed class OkxOrderBookSnapshot : IMarketOrderBookSnapshot
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
/// Торговый сигнал OKX.
/// </summary>
public sealed class OkxTradeSignal : IMarketTradeSignal
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
/// Исключение операций OKX.
/// </summary>
public sealed class OkxException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v5

/// <summary>
/// WebSocket-клиент OKX v5 для получения рыночных данных.
/// </summary>
/// <remarks>
/// Подключается к wss://ws.okx.com:8443/ws/v5/public
/// Каналы: tickers, books, trades, mark-price.
/// </remarks>
public class OkxClient : ExchangeClientBase
{
    /// <summary>
    /// Стандартный публичный WebSocket endpoint OKX v5.
    /// </summary>
    public const string DefaultWsUrl = "wss://ws.okx.com:8443/ws/v5/public";

    /// <summary>
    /// Создаёт WebSocket-клиент OKX v5 для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public OkxClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "OKX";

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

            if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, "notice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, "channel-conn-count-error", StringComparison.OrdinalIgnoreCase))
            {
                var code = MarketJsonParsingHelpers.TryGetString(root, "code");
                var message = MarketJsonParsingHelpers.TryGetString(root, "msg") ?? eventName;

                await PublishRuntimeErrorAsync(new OkxException($"OKX WebSocket {eventName} {code}: {message}".Trim())).ConfigureAwait(false);
                return;
            }

            if (string.Equals(eventName, "unsubscribe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, "channel-conn-count", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (!root.TryGetProperty("arg", out var argProperty)
            || argProperty.ValueKind is not JsonValueKind.Object
            || !MarketJsonParsingHelpers.PropertyEquals(argProperty, "channel", "tickers")
            || !root.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var tickerElement in dataProperty.EnumerateArray())
        {
            if (!tickerElement.TryGetProperty("instId", out var instIdProperty))
                continue;

            var assetId = MarketJsonParsingHelpers.TryGetString(instIdProperty);
            if (string.IsNullOrWhiteSpace(assetId))
                continue;

            var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "bidPx");
            var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickerElement, "askPx");
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
        if (!root.TryGetProperty("arg", out var argProperty)
            || argProperty.ValueKind is not JsonValueKind.Object
            || !argProperty.TryGetProperty("instId", out var instIdProperty))
        {
            return [];
        }

        var instId = MarketJsonParsingHelpers.TryGetString(instIdProperty);
        return string.IsNullOrWhiteSpace(instId) ? [] : [instId];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string op, string[] marketIds)
    {
        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append("{\"op\":\"");
        builder.Append(op);
        builder.Append("\",\"args\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append("{\"channel\":\"tickers\",\"instId\":\"");
            builder.Append(marketIds[index]);
            builder.Append("\"}");
        }

        builder.Append("]}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

#endregion

#region IMarketRestClient — REST API v5

/// <summary>
/// REST-клиент OKX API v5.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v5/market/ticker, GET /api/v5/market/books
/// Приватные: POST /api/v5/trade/order — HMAC-SHA256 + timestamp подпись.
/// </remarks>
public sealed class OkxRestClient : IMarketRestClient, IDisposable
{
    /// <summary>
    /// Стандартный базовый URL REST API OKX v5.
    /// </summary>
    public const string DefaultApiUrl = "https://www.okx.com";

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
    /// Создаёт REST-клиент OKX v5.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="passphrase">Passphrase аккаунта OKX.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public OkxRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, string? passphrase = null,
        IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Okx(apiKey, apiSecret, passphrase ?? "") : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v5/trade/order
        const string path = "/api/v5/trade/order";
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? "limit" : "market";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);

        var bodyObj = new Dictionary<string, string>
        {
            ["instId"] = assetId,
            ["tdMode"] = "cash",
            ["side"] = sideStr,
            ["ordType"] = orderType,
            ["sz"] = qtyStr
        };

        if (price.HasValue)
            bodyObj["px"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new OkxException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OkxException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            return data[0].TryGetProperty("ordId", out var ordId) ? MarketJsonParsingHelpers.TryGetString(ordId) : null;

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /api/v5/trade/cancel-order
        // orderId в формате "INST_ID:ORDER_ID"
        const string path = "/api/v5/trade/cancel-order";
        var parts = orderId.Split(':', 2);
        var (instId, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new OkxException("orderId должен быть в формате 'INST_ID:ORDER_ID'.");

        var bodyObj = new Dictionary<string, string> { ["instId"] = instId, ["ordId"] = oid };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        if (authenticator is null)
            throw new OkxException("Аутентификация не настроена. Укажите apiKey/apiSecret или IMarketAuthenticator.");
        authenticator.SignRequest(request, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v5/market/ticker?instId=BTC-USDT
        var response = await httpClient.GetAsync(
            $"/api/v5/market/ticker?instId={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;

        return MarketJsonParsingHelpers.TryParseDouble(data[0], "last");
    }

    /// <inheritdoc />
    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v5/market/books?instId=BTC-USDT&sz=20
        var response = await httpClient.GetAsync(
            $"/api/v5/market/books?instId={Uri.EscapeDataString(assetId)}&sz=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;
        var book = data[0];

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

        var tsMs = MarketJsonParsingHelpers.TryParseInt64(book, "ts") ?? 0;

        return new OkxOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs),
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
/// Кеш цен OKX.
/// </summary>
public sealed class OkxPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, OkxPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен OKX без runtime-подписки.
    /// </summary>
    public OkxPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен OKX и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент OKX, публикующий обновления рынка.</param>
    public OkxPriceStream(OkxClient client)
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
            _ => new OkxPriceSnapshot
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
    public void UpdatePrice(string instId, double bid, double ask, double lastTrade)
    {
        SetPrice(instId, new OkxPriceSnapshot
        {
            AssetId = instId,
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

[JsonSerializable(typeof(OkxPriceSnapshot))]
[JsonSerializable(typeof(OkxPosition))]
[JsonSerializable(typeof(OkxPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class OkxJsonContext : JsonSerializerContext;

#endregion
