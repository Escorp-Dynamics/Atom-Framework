using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Kraken.
/// </summary>
public sealed class KrakenPosition : IMarketPosition
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
/// Сводка портфеля Kraken.
/// </summary>
public sealed class KrakenPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Kraken.
/// </summary>
public sealed class KrakenOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Kraken.
/// </summary>
public sealed class KrakenTradeSignal : IMarketTradeSignal
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
public sealed class KrakenClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://ws.kraken.com/v2";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Kraken";
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

        // Kraken v2: { "method": "subscribe", "params": { "channel": "ticker", "symbol": ["BTC/USD"] } }
        var msg = JsonSerializer.Serialize(new
        {
            method = "subscribe",
            @params = new { channel = "ticker", symbol = marketIds }
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var msg = JsonSerializer.Serialize(new
        {
            method = "unsubscribe",
            @params = new { channel = "ticker", symbol = marketIds }
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
    public const string DefaultApiUrl = "https://api.kraken.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly byte[]? secretBytes;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

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
    public KrakenRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;

        if (apiKey is not null)
            this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation("API-Key", apiKey);

        secretBytes = apiSecret is not null ? Convert.FromBase64String(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Kraken(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Генерирует Kraken HMAC-SHA512 подпись: SHA256(nonce + postData) → HMAC-SHA512(urlPath + sha256hash).
    /// </summary>
    private byte[] Sign(string urlPath, string nonce, string postData)
    {
        if (secretBytes is null)
            throw new KrakenException("apiSecret не задан. Подпись невозможна.");

        var sha256Hash = SHA256.HashData(Encoding.UTF8.GetBytes(nonce + postData));
        var pathBytes = Encoding.UTF8.GetBytes(urlPath);
        var message = new byte[pathBytes.Length + sha256Hash.Length];
        pathBytes.CopyTo(message, 0);
        sha256Hash.CopyTo(message, pathBytes.Length);
        return HMACSHA512.HashData(secretBytes, message);
    }

    /// <summary>Генерирует уникальный nonce на основе Unix-timestamp (микросекунды).</summary>
    private static string GetNonce() => (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString(CultureInfo.InvariantCulture);

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /0/private/AddOrder
        const string urlPath = "/0/private/AddOrder";
        var nonce = GetNonce();
        var sideStr = side == TradeSide.Buy ? "buy" : "sell";
        var orderType = price.HasValue ? "limit" : "market";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);

        var postData = $"nonce={nonce}&pair={Uri.EscapeDataString(assetId)}" +
                       $"&type={sideStr}&ordertype={orderType}&volume={qtyStr}";

        if (price.HasValue)
            postData += $"&price={price.Value.ToString("G", CultureInfo.InvariantCulture)}";

        var signature = Convert.ToBase64String(Sign(urlPath, nonce, postData));

        var request = new HttpRequestMessage(HttpMethod.Post, urlPath)
        {
            Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TryAddWithoutValidation("API-Sign", signature);

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

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /0/private/CancelOrder
        const string urlPath = "/0/private/CancelOrder";
        var nonce = GetNonce();
        var postData = $"nonce={nonce}&txid={Uri.EscapeDataString(orderId)}";
        var signature = Convert.ToBase64String(Sign(urlPath, nonce, postData));

        var request = new HttpRequestMessage(HttpMethod.Post, urlPath)
        {
            Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.TryAddWithoutValidation("API-Sign", signature);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

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
            var priceStr = closes[0].GetString();
            if (double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                return p;
        }

        return null;
    }

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
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
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
public sealed class KrakenPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, KrakenPriceSnapshot> cache = new();
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
            _ => new KrakenPriceSnapshot
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

[JsonSerializable(typeof(KrakenPriceSnapshot))]
[JsonSerializable(typeof(KrakenPosition))]
[JsonSerializable(typeof(KrakenPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class KrakenJsonContext : JsonSerializerContext;

#endregion
