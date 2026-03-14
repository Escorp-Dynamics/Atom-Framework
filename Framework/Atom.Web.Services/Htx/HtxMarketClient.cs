using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Htx;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для HTX (бывш. Huobi) Spot API v1.
// WebSocket (wss://api.huobi.pro/ws) + REST API.
// HMAC-SHA256 подпись ордеров.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены HTX.</summary>
public sealed class HtxPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на HTX.</summary>
public sealed class HtxPosition : IMarketPosition
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

/// <summary>Сводка портфеля HTX.</summary>
public sealed class HtxPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров HTX.</summary>
public sealed class HtxOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал HTX.</summary>
public sealed class HtxTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций HTX.</summary>
public sealed class HtxException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент HTX для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://api.huobi.pro/ws — gzip-сжатые сообщения, обязательный pong.
/// Подписка: { "sub": "market.btcusdt.ticker", "id": "id1" }.
/// </remarks>
public sealed class HtxClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://api.huobi.pro/ws";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "HTX";
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

        // HTX: подписка на каждый символ отдельно
        foreach (var sym in marketIds)
        {
            var msg = JsonSerializer.Serialize(new { sub = $"market.{sym.ToLowerInvariant()}.ticker", id = sym });
            var bytes = Encoding.UTF8.GetBytes(msg);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        foreach (var sym in marketIds)
        {
            var msg = JsonSerializer.Serialize(new { unsub = $"market.{sym.ToLowerInvariant()}.ticker", id = sym });
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

#region IMarketRestClient — REST API + HMAC-SHA256

/// <summary>
/// REST-клиент HTX Spot API.
/// </summary>
/// <remarks>
/// Публичные: GET /market/detail/merged, GET /market/depth
/// Приватные: POST /v1/order/orders/place — HMAC-SHA256 подпись в query-параметрах.
/// Особенность: подпись формируется из HOST + METHOD + PATH + отсортированных query-параметров.
/// </remarks>
public sealed class HtxRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://api.huobi.pro";
    private const string ApiHost = "api.huobi.pro";

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
    /// Создаёт REST-клиент HTX.
    /// </summary>
    public HtxRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Htx(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// HTX HMAC-SHA256 подпись.
    /// Строка: METHOD\nHOST\nPATH\nОтсортированные query-параметры.
    /// </summary>
    private string Sign(string method, string path, SortedDictionary<string, string> parameters)
    {
        if (secretBytes is null)
            throw new HtxException("apiSecret не задан.");

        var queryString = string.Join("&", parameters.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var preSign = $"{method}\n{ApiHost}\n{path}\n{queryString}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToBase64String(hash);
    }

    /// <summary>Общие параметры аутентификации.</summary>
    private SortedDictionary<string, string> BuildAuthParams()
    {
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = apiKey ?? "",
            ["SignatureMethod"] = "HmacSHA256",
            ["SignatureVersion"] = "2",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
        };
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // 1) Получаем account-id (упрощённо — в реальном коде кешируется)
        var accountId = await GetSpotAccountIdAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new HtxException("Не удалось получить account-id.");

        // 2) Формируем подписанный запрос
        const string path = "/v1/order/orders/place";
        var authParams = BuildAuthParams();
        var signature = Sign("POST", path, authParams);
        authParams["Signature"] = signature;

        var queryStr = string.Join("&", authParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? $"{sideStr}-limit" : $"{sideStr}-market";

        var bodyObj = new Dictionary<string, string>
        {
            ["account-id"] = accountId,
            ["symbol"] = assetId.ToLowerInvariant(),
            ["type"] = orderType,
            ["amount"] = quantity.ToString("G", CultureInfo.InvariantCulture)
        };

        if (price.HasValue)
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{path}?{queryStr}")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HtxException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("data", out var data) ? data.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var path = $"/v1/order/orders/{Uri.EscapeDataString(orderId)}/submitcancel";
        var authParams = BuildAuthParams();
        var signature = Sign("POST", path, authParams);
        authParams["Signature"] = signature;

        var queryStr = string.Join("&", authParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var request = new HttpRequestMessage(HttpMethod.Post, $"{path}?{queryStr}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Получает ID полне спот-аккаунта.</summary>
    private async ValueTask<string?> GetSpotAccountIdAsync(CancellationToken cancellationToken)
    {
        const string path = "/v1/account/accounts";
        var authParams = BuildAuthParams();
        var signature = Sign("GET", path, authParams);
        authParams["Signature"] = signature;

        var queryStr = string.Join("&", authParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var response = await httpClient.GetAsync($"{path}?{queryStr}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var arr)) return null;

        foreach (var acct in arr.EnumerateArray())
        {
            if (acct.TryGetProperty("type", out var t) && t.GetString() == "spot"
                && acct.TryGetProperty("id", out var id))
                return id.GetInt64().ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/market/detail/merged?symbol={Uri.EscapeDataString(assetId.ToLowerInvariant())}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tick", out var tick)) return null;

        if (tick.TryGetProperty("close", out var close))
            return close.GetDouble();

        return null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/market/depth?symbol={Uri.EscapeDataString(assetId.ToLowerInvariant())}&type=step0&depth=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tick", out var tick)) return null;

        static (double Price, double Qty)[] ParseLevels(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var arr)) return [];
            var list = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < list.Length; i++)
            {
                var level = arr[i];
                list[i] = (level[0].GetDouble(), level[1].GetDouble());
            }
            return list;
        }

        return new HtxOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(tick, "bids"),
            Asks = ParseLevels(tick, "asks")
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

/// <summary>Кеш цен HTX.</summary>
public sealed class HtxPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, HtxPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new HtxPriceSnapshot
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

[JsonSerializable(typeof(HtxPriceSnapshot))]
[JsonSerializable(typeof(HtxPosition))]
[JsonSerializable(typeof(HtxPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class HtxJsonContext : JsonSerializerContext;

#endregion
