using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Mexc;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для MEXC Spot API v3.
// WebSocket (wss://wbs.mexc.com/ws) + REST API.
// HMAC-SHA256 подпись ордеров (аналог Binance-совместимого API).
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены MEXC.</summary>
public sealed class MexcPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на MEXC.</summary>
public sealed class MexcPosition : IMarketPosition
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

/// <summary>Сводка портфеля MEXC.</summary>
public sealed class MexcPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров MEXC.</summary>
public sealed class MexcOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал MEXC.</summary>
public sealed class MexcTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций MEXC.</summary>
public sealed class MexcException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент MEXC для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://wbs.mexc.com/ws — JSON-сообщения.
/// Подписка: { "method": "SUBSCRIPTION", "params": ["spot@public.miniTicker.v3.api@BTCUSDT"] }.
/// </remarks>
public sealed class MexcClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://wbs.mexc.com/ws";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "MEXC";
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

        // MEXC v3 подписка
        var channels = marketIds.Select(s => $"spot@public.miniTicker.v3.api@{s}").ToArray();
        var msg = JsonSerializer.Serialize(new { method = "SUBSCRIPTION", @params = channels });

        var bytes = Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var channels = marketIds.Select(s => $"spot@public.miniTicker.v3.api@{s}").ToArray();
        var msg = JsonSerializer.Serialize(new { method = "UNSUBSCRIPTION", @params = channels });

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
/// REST-клиент MEXC Spot API v3.
/// </summary>
/// <remarks>
/// API совместим с Binance: /api/v3/order, HMAC-SHA256 signature в query.
/// Параметры: symbol, side, type, quantity, price, timestamp, signature.
/// </remarks>
public sealed class MexcRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://api.mexc.com";

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
    /// Создаёт REST-клиент MEXC.
    /// </summary>
    public MexcRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Mexc(apiKey, apiSecret) : null);
    }

    /// <summary>HMAC-SHA256 hex-подпись (Binance-совместимая).</summary>
    private string Sign(string queryString)
    {
        if (secretBytes is null)
            throw new MexcException("apiSecret не задан.");

        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(queryString));
        return Convert.ToHexStringLower(hash);
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";
        var ts = GetTimestamp();

        var sb = new StringBuilder();
        sb.Append("symbol=").Append(Uri.EscapeDataString(assetId));
        sb.Append("&side=").Append(sideStr);
        sb.Append("&type=").Append(orderType);
        sb.Append("&quantity=").Append(quantity.ToString("G", CultureInfo.InvariantCulture));

        if (price.HasValue)
        {
            sb.Append("&price=").Append(price.Value.ToString("G", CultureInfo.InvariantCulture));
            sb.Append("&timeInForce=GTC");
        }

        sb.Append("&timestamp=").Append(ts);

        var queryString = sb.ToString();
        var sig = Sign(queryString);
        queryString += $"&signature={sig}";

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v3/order?{queryString}");
        request.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", apiKey ?? "");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MexcException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("orderId", out var orderId) ? orderId.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // Формат orderId: "SYMBOL:ORDER_ID"
        var parts = orderId.Split(':', 2);
        if (parts.Length != 2)
            throw new MexcException($"orderId должен быть в формате SYMBOL:ORDER_ID, получено: {orderId}");

        var symbol = parts[0];
        var oid = parts[1];
        var ts = GetTimestamp();

        var queryString = $"symbol={Uri.EscapeDataString(symbol)}&orderId={Uri.EscapeDataString(oid)}&timestamp={ts}";
        var sig = Sign(queryString);
        queryString += $"&signature={sig}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{queryString}");
        request.Headers.TryAddWithoutValidation("X-MEXC-APIKEY", apiKey ?? "");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v3/ticker/price?symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var priceStr = doc.RootElement.TryGetProperty("price", out var p) ? p.GetString() : null;
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ? val : null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v3/depth?symbol={Uri.EscapeDataString(assetId)}&limit=20",
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

        return new MexcOrderBookSnapshot
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

/// <summary>Кеш цен MEXC.</summary>
public sealed class MexcPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, MexcPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new MexcPriceSnapshot
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

[JsonSerializable(typeof(MexcPriceSnapshot))]
[JsonSerializable(typeof(MexcPosition))]
[JsonSerializable(typeof(MexcPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class MexcJsonContext : JsonSerializerContext;

#endregion
