using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.CryptoCom;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для Crypto.com Exchange API v1.
// WebSocket (wss://stream.crypto.com/exchange/v1/market) + REST API v1.
// HMAC-SHA256 подпись ордеров.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены Crypto.com.</summary>
public sealed class CryptoComPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на Crypto.com.</summary>
public sealed class CryptoComPosition : IMarketPosition
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

/// <summary>Сводка портфеля Crypto.com.</summary>
public sealed class CryptoComPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров Crypto.com.</summary>
public sealed class CryptoComOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал Crypto.com.</summary>
public sealed class CryptoComTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций Crypto.com.</summary>
public sealed class CryptoComException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Crypto.com для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://stream.crypto.com/exchange/v1/market
/// Подписка: { "method":"subscribe","params":{"channels":["ticker.BTC_USDT"]},"id":1 }
/// </remarks>
public sealed class CryptoComClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://stream.crypto.com/exchange/v1/market";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Crypto.com";
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

        var channels = marketIds.Select(s => $"ticker.{s}").ToArray();
        var msg = JsonSerializer.Serialize(new
        {
            method = "subscribe",
            @params = new { channels },
            id = 1
        });

        var bytes = Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var channels = marketIds.Select(s => $"ticker.{s}").ToArray();
        var msg = JsonSerializer.Serialize(new
        {
            method = "unsubscribe",
            @params = new { channels },
            id = 2
        });

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
/// REST-клиент Crypto.com Exchange API v1.
/// </summary>
/// <remarks>
/// Crypto.com: HMAC-SHA256 подпись: method + id + apiKey + params (отсортированные) + nonce.
/// POST /exchange/v1/private/create-order, POST /exchange/v1/private/cancel-order.
/// GET /exchange/v1/public/get-ticker, GET /exchange/v1/public/get-book.
/// </remarks>
public sealed class CryptoComRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://api.crypto.com";

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
    /// Создаёт REST-клиент Crypto.com.
    /// </summary>
    public CryptoComRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.CryptoCom(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Crypto.com HMAC-SHA256: method + id + apiKey + отсортированные params + nonce.
    /// </summary>
    private string Sign(string method, int id, string nonce, SortedDictionary<string, object>? parameters = null)
    {
        if (secretBytes is null || apiKey is null)
            throw new CryptoComException("apiKey/apiSecret не заданы.");

        var paramStr = "";
        if (parameters is not null)
        {
            paramStr = string.Join("", parameters.Select(kv => $"{kv.Key}{kv.Value}"));
        }

        var preSign = $"{method}{id}{apiKey}{paramStr}{nonce}";
        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        const string method = "private/create-order";
        var id = 1;
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";

        var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["instrument_name"] = assetId,
            ["side"] = sideStr,
            ["type"] = orderType,
            ["quantity"] = quantity.ToString("G", CultureInfo.InvariantCulture)
        };

        if (price.HasValue)
            parameters["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var sig = Sign(method, id, nonce, parameters);

        var bodyObj = new Dictionary<string, object>
        {
            ["id"] = id,
            ["method"] = method,
            ["api_key"] = apiKey!,
            ["params"] = parameters,
            ["nonce"] = long.Parse(nonce, CultureInfo.InvariantCulture),
            ["sig"] = sig
        };

        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/exchange/v1/{method}")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new CryptoComException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("order_id", out var orderId)
            ? orderId.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // Формат orderId: "INSTRUMENT:ORDER_ID"
        var parts = orderId.Split(':', 2);
        if (parts.Length != 2)
            throw new CryptoComException($"orderId в формате INSTRUMENT:ORDER_ID, получено: {orderId}");

        const string method = "private/cancel-order";
        var id = 2;
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["instrument_name"] = parts[0],
            ["order_id"] = parts[1]
        };

        var sig = Sign(method, id, nonce, parameters);

        var bodyObj = new Dictionary<string, object>
        {
            ["id"] = id,
            ["method"] = method,
            ["api_key"] = apiKey!,
            ["params"] = parameters,
            ["nonce"] = long.Parse(nonce, CultureInfo.InvariantCulture),
            ["sig"] = sig
        };

        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/exchange/v1/{method}")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/exchange/v1/public/get-ticker?instrument_name={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data", out var data)) return null;

        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var ticker = data[0];
            return ticker.TryGetProperty("a", out var ask) && side == TradeSide.Buy ? ask.GetDouble()
                : ticker.TryGetProperty("b", out var bid) ? bid.GetDouble() : null;
        }

        return null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/exchange/v1/public/get-book?instrument_name={Uri.EscapeDataString(assetId)}&depth=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data", out var data)) return null;

        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;
        var book = data[0];

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

        return new CryptoComOrderBookSnapshot
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

/// <summary>Кеш цен Crypto.com.</summary>
public sealed class CryptoComPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, CryptoComPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(symbol,
            _ => new CryptoComPriceSnapshot
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

[JsonSerializable(typeof(CryptoComPriceSnapshot))]
[JsonSerializable(typeof(CryptoComPosition))]
[JsonSerializable(typeof(CryptoComPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CryptoComJsonContext : JsonSerializerContext;

#endregion
