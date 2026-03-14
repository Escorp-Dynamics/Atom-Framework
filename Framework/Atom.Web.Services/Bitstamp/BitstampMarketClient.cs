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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на Bitstamp.</summary>
public sealed class BitstampPosition : IMarketPosition
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

/// <summary>Сводка портфеля Bitstamp.</summary>
public sealed class BitstampPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров Bitstamp.</summary>
public sealed class BitstampOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал Bitstamp.</summary>
public sealed class BitstampTradeSignal : IMarketTradeSignal
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
public sealed class BitstampClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://ws.bitstamp.net";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Bitstamp";
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

        foreach (var sym in marketIds)
        {
            var msg = JsonSerializer.Serialize(new
            {
                @event = "bts:subscribe",
                data = new { channel = $"live_trades_{sym}" }
            });

            var bytes = Encoding.UTF8.GetBytes(msg);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        foreach (var sym in marketIds)
        {
            var msg = JsonSerializer.Serialize(new
            {
                @event = "bts:unsubscribe",
                data = new { channel = $"live_trades_{sym}" }
            });

            var bytes = Encoding.UTF8.GetBytes(msg);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
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
    public const string DefaultApiUrl = "https://www.bitstamp.net";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string? apiKey;
    private readonly byte[]? secretBytes;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Bitstamp.
    /// </summary>
    public BitstampRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bitstamp(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Bitstamp HMAC-SHA256 подпись v2.
    /// Строка: "BITSTAMP {apiKey}" + HMAC(timestamp + nonce + Content-Type + path + query + body).
    /// </summary>
    private string Sign(string timestamp, string nonce, string contentType, string path, string query, string body)
    {
        if (secretBytes is null)
            throw new BitstampException("apiSecret не задан.");

        var preSign = $"BITSTAMP {apiKey}{timestamp}{nonce}{contentType}{path}{query}{body}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    private void AddAuthHeaders(HttpRequestMessage request, string path, string body = "", string contentType = "")
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");
        var query = request.RequestUri?.Query.TrimStart('?') ?? "";

        var signature = Sign(timestamp, nonce, contentType, path, query, body);

        request.Headers.TryAddWithoutValidation("X-Auth", $"BITSTAMP {apiKey}");
        request.Headers.TryAddWithoutValidation("X-Auth-Signature", signature);
        request.Headers.TryAddWithoutValidation("X-Auth-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Auth-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Auth-Version", "v2");
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
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
        AddAuthHeaders(request, path, body, "application/x-www-form-urlencoded");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BitstampException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        const string path = "/api/v2/cancel_order/";
        var formData = new Dictionary<string, string> { ["id"] = orderId };
        var content = new FormUrlEncodedContent(formData);
        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        AddAuthHeaders(request, path, body, "application/x-www-form-urlencoded");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v2/ticker/{Uri.EscapeDataString(assetId)}/",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var priceStr = doc.RootElement.TryGetProperty("last", out var last) ? last.GetString() : null;
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

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
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
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
public sealed class BitstampPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BitstampPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new BitstampPriceSnapshot
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

[JsonSerializable(typeof(BitstampPriceSnapshot))]
[JsonSerializable(typeof(BitstampPosition))]
[JsonSerializable(typeof(BitstampPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BitstampJsonContext : JsonSerializerContext;

#endregion
