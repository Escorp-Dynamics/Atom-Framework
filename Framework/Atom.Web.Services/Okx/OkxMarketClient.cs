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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на OKX.
/// </summary>
public sealed class OkxPosition : IMarketPosition
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
/// Сводка портфеля OKX.
/// </summary>
public sealed class OkxPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров OKX.
/// </summary>
public sealed class OkxOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал OKX.
/// </summary>
public sealed class OkxTradeSignal : IMarketTradeSignal
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
public sealed class OkxClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://ws.okx.com:8443/ws/v5/public";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "OKX";
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

        // OKX v5: { "op": "subscribe", "args": [{ "channel": "tickers", "instId": "BTC-USDT" }] }
        var args = marketIds.Select(id => new { channel = "tickers", instId = id }).ToArray();
        var msg = JsonSerializer.Serialize(new { op = "subscribe", args });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var args = marketIds.Select(id => new { channel = "tickers", instId = id }).ToArray();
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
/// REST-клиент OKX API v5.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v5/market/ticker, GET /api/v5/market/books
/// Приватные: POST /api/v5/trade/order — HMAC-SHA256 + timestamp подпись.
/// </remarks>
public sealed class OkxRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://www.okx.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly byte[]? secretBytes;
    private readonly string? passphrase;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    public string BaseUrl { get; }

    /// <summary>
    /// Создаёт REST-клиент OKX v5.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="passphrase">Passphrase аккаунта OKX.</param>
    public OkxRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, string? passphrase = null,
        IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.passphrase = passphrase;

        if (apiKey is not null)
            this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation("OK-ACCESS-KEY", apiKey);

        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Okx(apiKey, apiSecret, passphrase ?? "") : null);
    }

    /// <summary>
    /// Генерирует OKX HMAC-SHA256 подпись (base64): timestamp + method + path + body.
    /// </summary>
    private string Sign(string timestamp, string method, string requestPath, string body = "")
    {
        if (secretBytes is null)
            throw new OkxException("apiSecret не задан. Подпись невозможна.");

        var preSign = $"{timestamp}{method}{requestPath}{body}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToBase64String(hash);
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Добавляет заголовки аутентификации OKX.
    /// </summary>
    private void AddAuthHeaders(HttpRequestMessage request, string timestamp, string method, string path, string body = "")
    {
        request.Headers.TryAddWithoutValidation("OK-ACCESS-SIGN", Sign(timestamp, method, path, body));
        request.Headers.TryAddWithoutValidation("OK-ACCESS-TIMESTAMP", timestamp);
        request.Headers.TryAddWithoutValidation("OK-ACCESS-PASSPHRASE", passphrase ?? "");
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v5/trade/order
        const string path = "/api/v5/trade/order";
        var ts = GetTimestamp();
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
        AddAuthHeaders(request, ts, "POST", path, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OkxException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            return data[0].TryGetProperty("ordId", out var ordId) ? ordId.GetString() : null;

        return null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /api/v5/trade/cancel-order
        // orderId в формате "INST_ID:ORDER_ID"
        const string path = "/api/v5/trade/cancel-order";
        var parts = orderId.Split(':', 2);
        var (instId, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new OkxException("orderId должен быть в формате 'INST_ID:ORDER_ID'.");

        var ts = GetTimestamp();
        var bodyObj = new Dictionary<string, string> { ["instId"] = instId, ["ordId"] = oid };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(request, ts, "POST", path, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

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

        var priceStr = data[0].GetProperty("last").GetString();
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

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
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
            }
            return list;
        }

        var tsStr = book.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;
        long.TryParse(tsStr, out var tsMs);

        return new OkxOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs),
            Bids = ParseLevels(book, "bids"),
            Asks = ParseLevels(book, "asks")
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
/// Кеш цен OKX.
/// </summary>
public sealed class OkxPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, OkxPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <summary>
    /// Обновляет кеш (вызывается из WebSocket receive loop).
    /// </summary>
    public void UpdatePrice(string instId, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(instId,
            _ => new OkxPriceSnapshot
            {
                AssetId = instId,
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

[JsonSerializable(typeof(OkxPriceSnapshot))]
[JsonSerializable(typeof(OkxPosition))]
[JsonSerializable(typeof(OkxPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class OkxJsonContext : JsonSerializerContext;

#endregion
