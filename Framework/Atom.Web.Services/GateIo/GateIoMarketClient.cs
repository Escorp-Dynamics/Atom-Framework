using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.GateIo;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Gate.io API v4.
// WebSocket (wss://api.gateio.ws/ws/v4/) + REST v4.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Gate.io.
/// </summary>
public sealed class GateIoPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Gate.io.
/// </summary>
public sealed class GateIoPosition : IMarketPosition
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
/// Сводка портфеля Gate.io.
/// </summary>
public sealed class GateIoPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Gate.io.
/// </summary>
public sealed class GateIoOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Gate.io.
/// </summary>
public sealed class GateIoTradeSignal : IMarketTradeSignal
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
/// Исключение операций Gate.io.
/// </summary>
public sealed class GateIoException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v4

/// <summary>
/// WebSocket-клиент Gate.io v4.
/// </summary>
/// <remarks>
/// Подключается к wss://api.gateio.ws/ws/v4/
/// Каналы: spot.tickers, spot.order_book, spot.trades.
/// </remarks>
public sealed class GateIoClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://api.gateio.ws/ws/v4/";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Gate.io";
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

        // Gate.io v4: { "time": 123, "channel": "spot.tickers", "event": "subscribe", "payload": ["BTC_USDT"] }
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var msg = JsonSerializer.Serialize(new
        {
            time = ts,
            channel = "spot.tickers",
            @event = "subscribe",
            payload = marketIds
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var msg = JsonSerializer.Serialize(new
        {
            time = ts,
            channel = "spot.tickers",
            @event = "unsubscribe",
            payload = marketIds
        });

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

#region IMarketRestClient — REST API v4

/// <summary>
/// REST-клиент Gate.io API v4.
/// </summary>
/// <remarks>
/// Публичные: GET /api/v4/spot/tickers, GET /api/v4/spot/order_book
/// Приватные: POST /api/v4/spot/orders — HMAC-SHA512 подпись.
/// </remarks>
public sealed class GateIoRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://api.gateio.ws";

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
    /// Создаёт REST-клиент Gate.io v4.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA512 подписи.</param>
    public GateIoRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.GateIo(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Генерирует Gate.io HMAC-SHA512 подпись: method + path + query + SHA512(body) + timestamp.
    /// </summary>
    private string Sign(string method, string path, string query, string body, string timestamp)
    {
        if (secretBytes is null)
            throw new GateIoException("apiSecret не задан. Подпись невозможна.");

        var bodyHash = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(body)));
        var preSign = $"{method}\n{path}\n{query}\n{bodyHash}\n{timestamp}";
        var hash = HMACSHA512.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Добавляет заголовки аутентификации Gate.io.
    /// </summary>
    private void AddAuthHeaders(HttpRequestMessage request, string method, string path, string query, string body, string timestamp)
    {
        request.Headers.TryAddWithoutValidation("KEY", apiKey ?? "");
        request.Headers.TryAddWithoutValidation("SIGN", Sign(method, path, query, body, timestamp));
        request.Headers.TryAddWithoutValidation("Timestamp", timestamp);
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v4/spot/orders
        const string path = "/api/v4/spot/orders";
        var ts = GetTimestamp();
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);
        var orderType = price.HasValue ? "limit" : "market";

        var bodyObj = new Dictionary<string, string>
        {
            ["currency_pair"] = assetId,
            ["side"] = sideStr,
            ["type"] = orderType,
            ["amount"] = qtyStr
        };

        if (price.HasValue)
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(request, "POST", path, "", bodyJson, ts);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GateIoException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("id", out var idProp)
            ? idProp.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // DELETE /api/v4/spot/orders/{order_id}?currency_pair=...
        // orderId в формате "PAIR:ORDER_ID" (BTC_USDT:12345)
        var parts = orderId.Split(':', 2);
        var (pair, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new GateIoException("orderId должен быть в формате 'PAIR:ORDER_ID'.");

        var ts = GetTimestamp();
        var path = $"/api/v4/spot/orders/{Uri.EscapeDataString(oid)}";
        var query = $"currency_pair={Uri.EscapeDataString(pair)}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"{path}?{query}");
        AddAuthHeaders(request, "DELETE", path, query, "", ts);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v4/spot/tickers?currency_pair=BTC_USDT
        var response = await httpClient.GetAsync(
            $"/api/v4/spot/tickers?currency_pair={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        if (arr.GetArrayLength() == 0) return null;

        var priceStr = arr[0].GetProperty("last").GetString();
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v4/spot/order_book?currency_pair=BTC_USDT&limit=20
        var response = await httpClient.GetAsync(
            $"/api/v4/spot/order_book?currency_pair={Uri.EscapeDataString(assetId)}&limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

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

        return new GateIoOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(root, "bids"),
            Asks = ParseLevels(root, "asks")
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
/// Кеш цен Gate.io.
/// </summary>
public sealed class GateIoPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, GateIoPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <summary>
    /// Обновляет кеш (вызывается из WebSocket receive loop).
    /// </summary>
    public void UpdatePrice(string pair, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(pair,
            _ => new GateIoPriceSnapshot
            {
                AssetId = pair,
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

[JsonSerializable(typeof(GateIoPriceSnapshot))]
[JsonSerializable(typeof(GateIoPosition))]
[JsonSerializable(typeof(GateIoPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class GateIoJsonContext : JsonSerializerContext;

#endregion
