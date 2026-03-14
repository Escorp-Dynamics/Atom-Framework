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
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Coinbase.
/// </summary>
public sealed class CoinbasePosition : IMarketPosition
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
/// Сводка портфеля Coinbase.
/// </summary>
public sealed class CoinbasePortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Coinbase.
/// </summary>
public sealed class CoinbaseOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Coinbase.
/// </summary>
public sealed class CoinbaseTradeSignal : IMarketTradeSignal
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
public sealed class CoinbaseClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://advanced-trade-ws.coinbase.com";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Coinbase";
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

        // Advanced Trade WS: { "type": "subscribe", "product_ids": ["BTC-USD"], "channel": "ticker" }
        var msg = JsonSerializer.Serialize(new
        {
            type = "subscribe",
            product_ids = marketIds,
            channel = "ticker"
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var msg = JsonSerializer.Serialize(new
        {
            type = "unsubscribe",
            product_ids = marketIds,
            channel = "ticker"
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
    public const string DefaultApiUrl = "https://api.coinbase.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    public string BaseUrl { get; }

    /// <summary>
    /// Создаёт REST-клиент Coinbase Advanced Trade.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ (Cloud API key).</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
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

    /// <summary>
    /// Генерирует Coinbase HMAC-SHA256 подпись: timestamp + method + path + body.
    /// </summary>
    private string Sign(string timestamp, string method, string requestPath, string body = "")
    {
        if (secretBytes is null)
            throw new CoinbaseException("apiSecret не задан. Подпись невозможна.");

        var preSign = $"{timestamp}{method}{requestPath}{body}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    private static string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Добавляет заголовки аутентификации Coinbase.
    /// </summary>
    private void AddAuthHeaders(HttpRequestMessage request, string timestamp, string method, string path, string body = "")
    {
        request.Headers.TryAddWithoutValidation("CB-ACCESS-SIGN", Sign(timestamp, method, path, body));
        request.Headers.TryAddWithoutValidation("CB-ACCESS-TIMESTAMP", timestamp);
    }

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
            ? orderId.GetString() : null;
    }

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

        var priceStr = trades[0].GetProperty("price").GetString();
        return double.TryParse(priceStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

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
                double.TryParse(level.GetProperty("price").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level.GetProperty("size").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                list[i] = (p, q);
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
public sealed class CoinbasePriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, CoinbasePriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <summary>
    /// Обновляет кеш (вызывается из WebSocket receive loop).
    /// </summary>
    public void UpdatePrice(string productId, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(productId,
            _ => new CoinbasePriceSnapshot
            {
                AssetId = productId,
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

[JsonSerializable(typeof(CoinbasePriceSnapshot))]
[JsonSerializable(typeof(CoinbasePosition))]
[JsonSerializable(typeof(CoinbasePosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CoinbaseJsonContext : JsonSerializerContext;

#endregion
