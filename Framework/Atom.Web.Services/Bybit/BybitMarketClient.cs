using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Bybit.
/// </summary>
public sealed class BybitPosition : IMarketPosition
{
    public required string AssetId { get; init; }
    public double Quantity { get; set; }
    public double AverageCostBasis { get; set; }
    public double CurrentPrice { get; set; }
    public double MarketValue => Quantity * CurrentPrice;
    public double UnrealizedPnL => MarketValue - Quantity * AverageCostBasis;
    public double UnrealizedPnLPercent =>
        AverageCostBasis != 0 ? (UnrealizedPnL / (Quantity * AverageCostBasis)) * 100 : 0;
    public double RealizedPnL { get; set; }
    public double TotalFees { get; set; }
    public int TradeCount { get; set; }
    public bool IsClosed => Quantity <= 0;
}

/// <summary>
/// Сводка портфеля Bybit.
/// </summary>
public sealed class BybitPortfolioSummary : IMarketPortfolioSummary
{
    public int OpenPositions { get; init; }
    public int ClosedPositions { get; init; }
    public double TotalMarketValue { get; init; }
    public double TotalCostBasis { get; init; }
    public double TotalUnrealizedPnL { get; init; }
    public double TotalRealizedPnL { get; init; }
    public double TotalFees { get; init; }
    public double NetPnL => TotalUnrealizedPnL + TotalRealizedPnL - TotalFees;
}

/// <summary>
/// Книга ордеров Bybit.
/// </summary>
public sealed class BybitOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Bybit.
/// </summary>
public sealed class BybitTradeSignal : IMarketTradeSignal
{
    public required string AssetId { get; init; }
    public TradeAction Action { get; init; }
    public double Quantity { get; init; }
    public string? Price { get; init; }
    public double Confidence { get; init; }
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
public sealed class BybitClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://stream.bybit.com/v5/public/spot";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Bybit";
    public bool IsConnected => !isDisposed && socket?.State == WebSocketState.Open;

    public async ValueTask SubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (socket is null || socket.State != WebSocketState.Open)
        {
            socket?.Dispose();
            socket = new ClientWebSocket();
            cts = new CancellationTokenSource();
            await socket.ConnectAsync(new Uri(DefaultWsUrl), cancellationToken).ConfigureAwait(false);
        }

        // Bybit v5: { "op": "subscribe", "args": ["tickers.BTCUSDT", "tickers.ETHUSDT"] }
        var args = marketIds.Select(id => $"tickers.{id}").ToArray();
        var msg = JsonSerializer.Serialize(new { op = "subscribe", args });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var args = marketIds.Select(id => $"tickers.{id}").ToArray();
        var msg = JsonSerializer.Serialize(new { op = "unsubscribe", args });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (socket?.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", cancellationToken).ConfigureAwait(false);

        cts?.Dispose();
        cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        socket?.Dispose();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        cts?.Cancel();
        cts?.Dispose();
        socket?.Dispose();
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
    public const string DefaultApiUrl = "https://api.bybit.com";
    private const int RecvWindow = 5000;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string? apiKey;
    private readonly byte[]? secretBytes;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    public string BaseUrl { get; }

    /// <summary>
    /// Создаёт REST-клиент Bybit v5.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    public BybitRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bybit(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Генерирует Bybit HMAC-SHA256 подпись: timestamp + apiKey + recvWindow + paramStr.
    /// </summary>
    private string Sign(long timestamp, string paramStr)
    {
        if (secretBytes is null || apiKey is null)
            throw new BybitException("apiKey/apiSecret не заданы. Подпись невозможна.");

        var preSign = $"{timestamp}{apiKey}{RecvWindow}{paramStr}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    private static long GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /v5/order/create
        var ts = GetTimestamp();
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
        var signature = Sign(ts, bodyJson);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v5/order/create")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-BAPI-API-KEY", apiKey);
        request.Headers.TryAddWithoutValidation("X-BAPI-SIGN", signature);
        request.Headers.TryAddWithoutValidation("X-BAPI-TIMESTAMP", ts.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW", RecvWindow.ToString(CultureInfo.InvariantCulture));

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
                ? orderId.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /v5/order/cancel
        // orderId в формате "SYMBOL:ORDER_ID"
        var parts = orderId.Split(':', 2);
        var (symbol, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new BybitException("orderId должен быть в формате 'SYMBOL:ORDER_ID'.");

        var ts = GetTimestamp();
        var bodyObj = new Dictionary<string, string>
        {
            ["category"] = "spot",
            ["symbol"] = symbol,
            ["orderId"] = oid
        };
        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var signature = Sign(ts, bodyJson);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v5/order/cancel")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-BAPI-API-KEY", apiKey);
        request.Headers.TryAddWithoutValidation("X-BAPI-SIGN", signature);
        request.Headers.TryAddWithoutValidation("X-BAPI-TIMESTAMP", ts.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW", RecvWindow.ToString(CultureInfo.InvariantCulture));

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

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

        var priceStr = list[0].GetProperty("lastPrice").GetString();
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

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
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
            }
            return list;
        }

        var tsStr = result.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;
        long.TryParse(tsStr, out var tsMs);

        return new BybitOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs),
            Bids = ParseLevels(result, "b"),
            Asks = ParseLevels(result, "a")
        };
    }

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
public sealed class BybitPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BybitPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <summary>
    /// Обновляет кеш (вызывается из WebSocket receive loop).
    /// </summary>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new BybitPriceSnapshot
            {
                AssetId = symbol,
                BestBid = bid,
                BestAsk = ask,
                LastTradePrice = lastTrade,
                LastUpdateTicks = Environment.TickCount64
            },
            (_, existing) =>
            {
                existing.BestBid = bid;
                existing.BestAsk = ask;
                existing.LastTradePrice = lastTrade;
                existing.LastUpdateTicks = Environment.TickCount64;
                return existing;
            });
    }

    public void ClearCache() => cache.Clear();

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
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
