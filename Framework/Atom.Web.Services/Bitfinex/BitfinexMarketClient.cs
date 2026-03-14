using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Bitfinex;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Bitfinex API v2.
// WebSocket (wss://api-pub.bitfinex.com/ws/2) + REST v2.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Bitfinex.
/// </summary>
public sealed class BitfinexPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Bitfinex.
/// </summary>
public sealed class BitfinexPosition : IMarketPosition
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
/// Сводка портфеля Bitfinex.
/// </summary>
public sealed class BitfinexPortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Bitfinex.
/// </summary>
public sealed class BitfinexOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Bitfinex.
/// </summary>
public sealed class BitfinexTradeSignal : IMarketTradeSignal
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
/// Исключение операций Bitfinex.
/// </summary>
public sealed class BitfinexException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket v2

/// <summary>
/// WebSocket-клиент Bitfinex v2.
/// </summary>
/// <remarks>
/// Подключается к wss://api-pub.bitfinex.com/ws/2
/// Каналы: ticker, trades, book. Данные в массивном формате (не JSON-объекты).
/// </remarks>
public sealed class BitfinexClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://api-pub.bitfinex.com/ws/2";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Bitfinex";
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

        // Bitfinex v2: отдельный subscribe на каждый символ
        // { "event": "subscribe", "channel": "ticker", "symbol": "tBTCUSD" }
        foreach (var id in marketIds)
        {
            var msg = JsonSerializer.Serialize(new
            {
                @event = "subscribe",
                channel = "ticker",
                symbol = id
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        // Bitfinex: unsubscribe по chanId (упрощение — closeable через disconnect)
        foreach (var id in marketIds)
        {
            var msg = JsonSerializer.Serialize(new
            {
                @event = "unsubscribe",
                channel = "ticker",
                symbol = id
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
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

#region IMarketRestClient — REST API v2

/// <summary>
/// REST-клиент Bitfinex API v2.
/// </summary>
/// <remarks>
/// Публичные: GET /v2/ticker/{Symbol}, GET /v2/book/{Symbol}/P0
/// Приватные: POST /v2/auth/... — HMAC-SHA384 + nonce подпись.
/// </remarks>
public sealed class BitfinexRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://api-pub.bitfinex.com";

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
    /// Создаёт REST-клиент Bitfinex v2.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA384 подписи.</param>
    public BitfinexRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.apiKey = apiKey;
        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Bitfinex(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Генерирует Bitfinex HMAC-SHA384 подпись: /api/path + nonce + body.
    /// </summary>
    private string Sign(string path, string nonce, string body)
    {
        if (secretBytes is null)
            throw new BitfinexException("apiSecret не задан. Подпись невозможна.");

        var preSign = $"{path}{nonce}{body}";
        var hash = HMACSHA384.HashData(secretBytes, Encoding.UTF8.GetBytes(preSign));
        return Convert.ToHexStringLower(hash);
    }

    private static string GetNonce() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Добавляет заголовки аутентификации Bitfinex.
    /// </summary>
    private void AddAuthHeaders(HttpRequestMessage request, string path, string nonce, string body)
    {
        request.Headers.TryAddWithoutValidation("bfx-apikey", apiKey ?? "");
        request.Headers.TryAddWithoutValidation("bfx-nonce", nonce);
        request.Headers.TryAddWithoutValidation("bfx-signature", Sign(path, nonce, body));
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /v2/auth/w/order/submit
        const string path = "/v2/auth/w/order/submit";
        var nonce = GetNonce();
        var amount = side == TradeSide.Buy
            ? quantity.ToString("G", CultureInfo.InvariantCulture)
            : $"-{quantity.ToString("G", CultureInfo.InvariantCulture)}";
        var orderType = price.HasValue ? "EXCHANGE LIMIT" : "EXCHANGE MARKET";

        var bodyObj = new Dictionary<string, object>
        {
            ["type"] = orderType,
            ["symbol"] = assetId,
            ["amount"] = amount
        };

        if (price.HasValue)
            bodyObj["price"] = price.Value.ToString("G", CultureInfo.InvariantCulture);

        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(request, path, nonce, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BitfinexException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        // Ответ Bitfinex v2: [MTS, TYPE, MSG_ID, null, [ORDER_ID, ...]]
        var arr = doc.RootElement;
        if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 4)
        {
            var orderArr = arr[4];
            if (orderArr.ValueKind == JsonValueKind.Array && orderArr.GetArrayLength() > 0)
                return orderArr[0].GetInt64().ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // POST /v2/auth/w/order/cancel
        const string path = "/v2/auth/w/order/cancel";
        var nonce = GetNonce();

        if (!long.TryParse(orderId, out var id))
            throw new BitfinexException($"orderId должен быть числовым: '{orderId}'.");

        var bodyObj = new Dictionary<string, long> { ["id"] = id };
        var bodyJson = JsonSerializer.Serialize(bodyObj);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        AddAuthHeaders(request, path, nonce, bodyJson);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /v2/ticker/{Symbol} — возвращает массив [BID, BID_SIZE, ASK, ASK_SIZE, ...]
        var response = await httpClient.GetAsync(
            $"/v2/ticker/{Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        // [0] = BID, [2] = ASK, [6] = LAST_PRICE
        return arr.GetArrayLength() > 6
            ? arr[6].GetDouble()
            : null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /v2/book/{Symbol}/P0 — [[PRICE, COUNT, AMOUNT], ...]
        var response = await httpClient.GetAsync(
            $"/v2/book/{Uri.EscapeDataString(assetId)}/P0",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;

        var bids = new List<(double, double)>();
        var asks = new List<(double, double)>();

        for (int i = 0; i < arr.GetArrayLength(); i++)
        {
            var level = arr[i];
            var p = level[0].GetDouble();
            var amount = level[2].GetDouble();

            if (amount > 0)
                bids.Add((p, amount));
            else
                asks.Add((p, Math.Abs(amount)));
        }

        return new BitfinexOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = bids.ToArray(),
            Asks = asks.ToArray()
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
/// Кеш цен Bitfinex.
/// </summary>
public sealed class BitfinexPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BitfinexPriceSnapshot> cache = new();
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
            _ => new BitfinexPriceSnapshot
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

[JsonSerializable(typeof(BitfinexPriceSnapshot))]
[JsonSerializable(typeof(BitfinexPosition))]
[JsonSerializable(typeof(BitfinexPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BitfinexJsonContext : JsonSerializerContext;

#endregion
