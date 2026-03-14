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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на KuCoin.</summary>
public sealed class KuCoinPosition : IMarketPosition
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

/// <summary>Сводка портфеля KuCoin.</summary>
public sealed class KuCoinPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров KuCoin.</summary>
public sealed class KuCoinOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал KuCoin.</summary>
public sealed class KuCoinTradeSignal : IMarketTradeSignal
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
public sealed class KuCoinClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://ws-api-spot.kucoin.com";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "KuCoin";
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

        // KuCoin: { "id": "1", "type": "subscribe", "topic": "/market/ticker:BTC-USDT,ETH-USDT" }
        var topic = $"/market/ticker:{string.Join(",", marketIds)}";
        var msg = JsonSerializer.Serialize(new { id = "1", type = "subscribe", topic });

        var bytes = Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var topic = $"/market/ticker:{string.Join(",", marketIds)}";
        var msg = JsonSerializer.Serialize(new { id = "2", type = "unsubscribe", topic });

        var bytes = Encoding.UTF8.GetBytes(msg);
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
    public const string DefaultApiUrl = "https://api.kucoin.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string? apiKey;
    private readonly byte[]? secretBytes;
    private readonly string? passphrase;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент KuCoin.
    /// </summary>
    public KuCoinRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, string? passphrase = null,
        IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        this.passphrase = passphrase;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.KuCoin(apiKey, apiSecret, passphrase ?? "") : null);
    }

    /// <summary>
    /// HMAC-SHA256 подпись: timestamp + method + endpoint + body.
    /// </summary>
    private string Sign(string timestamp, string method, string endpoint, string body = "")
    {
        if (secretBytes is null)
            throw new KuCoinException("apiSecret не задан.");

        var preSign = $"{timestamp}{method}{endpoint}{body}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToBase64String(hash);
    }

    /// <summary>Подписанный passphrase (HMAC-SHA256 base64).</summary>
    private string SignPassphrase()
    {
        if (secretBytes is null || passphrase is null) return "";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(passphrase));
        return Convert.ToBase64String(hash);
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private void AddAuthHeaders(HttpRequestMessage request, string timestamp, string method, string endpoint, string body = "")
    {
        request.Headers.TryAddWithoutValidation("KC-API-KEY", apiKey ?? "");
        request.Headers.TryAddWithoutValidation("KC-API-SIGN", Sign(timestamp, method, endpoint, body));
        request.Headers.TryAddWithoutValidation("KC-API-TIMESTAMP", timestamp);
        request.Headers.TryAddWithoutValidation("KC-API-PASSPHRASE", SignPassphrase());
        request.Headers.TryAddWithoutValidation("KC-API-KEY-VERSION", "2");
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        const string endpoint = "/api/v1/orders";
        var ts = GetTimestamp();
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
        AddAuthHeaders(request, ts, "POST", endpoint, bodyJson);

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
                ? orderId.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var ts = GetTimestamp();
        var endpoint = $"/api/v1/orders/{Uri.EscapeDataString(orderId)}";

        var request = new HttpRequestMessage(HttpMethod.Delete, endpoint);
        AddAuthHeaders(request, ts, "DELETE", endpoint);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v1/market/stats?symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

        var priceStr = data.TryGetProperty("last", out var last) ? last.GetString() : null;
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

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
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
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
public sealed class KuCoinPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, KuCoinPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new KuCoinPriceSnapshot
            {
                AssetId = symbol, BestBid = bid, BestAsk = ask,
                LastTradePrice = lastTrade, LastUpdateTicks = Environment.TickCount64
            },
            (_, existing) =>
            {
                existing.BestBid = bid; existing.BestAsk = ask;
                existing.LastTradePrice = lastTrade; existing.LastUpdateTicks = Environment.TickCount64;
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

[JsonSerializable(typeof(KuCoinPriceSnapshot))]
[JsonSerializable(typeof(KuCoinPosition))]
[JsonSerializable(typeof(KuCoinPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class KuCoinJsonContext : JsonSerializerContext;

#endregion
