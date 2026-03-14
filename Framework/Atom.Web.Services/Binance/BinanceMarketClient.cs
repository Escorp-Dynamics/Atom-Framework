using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Binance;

// ═══════════════════════════════════════════════════════════════════
// Скелет реализации Markets/ для Binance Spot.
// Демонстрирует, как реализовать универсальные контракты для новой
// торговой платформы. Каждый блок помечен TODO для заполнения.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>
/// Снимок цены актива Binance.
/// </summary>
public sealed class BinancePriceSnapshot : IMarketPriceSnapshot
{
    /// <summary>Символ торговой пары (например BTCUSDT).</summary>
    public required string AssetId { get; init; }

    /// <summary>Лучшая цена покупки.</summary>
    public double? BestBid { get; set; }

    /// <summary>Лучшая цена продажи.</summary>
    public double? BestAsk { get; set; }

    /// <summary>Средняя цена.</summary>
    public double? Midpoint => (BestBid + BestAsk) / 2.0;

    /// <summary>Цена последней сделки.</summary>
    public double? LastTradePrice { get; set; }

    /// <summary>Время обновления (ticks).</summary>
    public long LastUpdateTicks { get; set; }
}

/// <summary>
/// Позиция на Binance.
/// </summary>
public sealed class BinancePosition : IMarketPosition
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
/// Сводка портфеля Binance.
/// </summary>
public sealed class BinancePortfolioSummary : IMarketPortfolioSummary
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
/// Книга ордеров Binance.
/// </summary>
public sealed class BinanceOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Заявки на покупку.</summary>
    public (double Price, double Quantity)[] Bids { get; init; } = [];

    /// <summary>Заявки на продажу.</summary>
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>
/// Торговый сигнал Binance.
/// </summary>
public sealed class BinanceTradeSignal : IMarketTradeSignal
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
/// Исключение операций Binance.
/// </summary>
public sealed class BinanceException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Binance Spot для получения рыночных данных в реальном времени.
/// </summary>
/// <remarks>
/// Подключается к wss://stream.binance.com:9443/ws
/// Поддерживает bookTicker, trade, kline и другие стримы.
/// </remarks>
public sealed class BinanceClient : IMarketClient, IDisposable
{
    /// <summary>Базовый URL WebSocket API.</summary>
    public const string DefaultWsUrl = "wss://stream.binance.com:9443/ws";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Binance";
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

        // TODO: Отправить SUBSCRIBE сообщение
        // { "method": "SUBSCRIBE", "params": ["btcusdt@bookTicker", ...], "id": 1 }
        var streams = marketIds.Select(id => $"{id.ToLowerInvariant()}@bookTicker");
        var msg = JsonSerializer.Serialize(new
        {
            method = "SUBSCRIBE",
            @params = streams.ToArray(),
            id = 1
        });

        var bytes = System.Text.Encoding.UTF8.GetBytes(msg);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var streams = marketIds.Select(id => $"{id.ToLowerInvariant()}@bookTicker");
        var msg = JsonSerializer.Serialize(new
        {
            method = "UNSUBSCRIBE",
            @params = streams.ToArray(),
            id = 2
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
/// REST-клиент Binance Spot API (v3).
/// </summary>
/// <remarks>
/// Поддерживает публичные и подписанные (HMAC-SHA256) эндпоинты.
/// Для приватных операций (ордера) требуется apiKey + apiSecret.
/// </remarks>
public sealed class BinanceRestClient : IMarketRestClient, IDisposable
{
    /// <summary>Базовый URL Binance Spot API.</summary>
    public const string DefaultApiUrl = "https://api.binance.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly byte[]? secretBytes;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Binance.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент (опционально).</param>
    /// <param name="apiKey">API-ключ для подписанных запросов.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    public BinanceRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;

        if (apiKey is not null)
            this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-MBX-APIKEY", apiKey);

        secretBytes = apiSecret is not null ? Encoding.UTF8.GetBytes(apiSecret) : null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Binance(apiKey, apiSecret) : null);
    }

    /// <summary>
    /// Генерирует HMAC-SHA256 подпись для строки запроса.
    /// </summary>
    private string Sign(string queryString)
    {
        if (secretBytes is null)
            throw new BinanceException("apiSecret не задан. Подпись невозможна.");

        var hash = HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(queryString));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Возвращает текущий Unix-timestamp в миллисекундах.
    /// </summary>
    private static long GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // POST /api/v3/order
        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";
        var qtyStr = quantity.ToString("G", CultureInfo.InvariantCulture);
        var ts = GetTimestamp();

        var query = $"symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}" +
                    $"&side={sideStr}" +
                    $"&type={orderType}" +
                    $"&quantity={qtyStr}" +
                    $"&timestamp={ts}";

        if (price.HasValue)
        {
            var priceStr = price.Value.ToString("G", CultureInfo.InvariantCulture);
            query += $"&timeInForce=GTC&price={priceStr}";
        }

        query += $"&signature={Sign(query)}";

        var content = new StringContent(query, Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await httpClient.PostAsync("/api/v3/order", content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new BinanceException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("orderId", out var orderId)
            ? orderId.GetInt64().ToString(CultureInfo.InvariantCulture) : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        // DELETE /api/v3/order — требуется symbol, но orderId глобально уникален в Binance.
        // Допущение: orderId передаётся в формате "SYMBOL:ORDER_ID" (напр. "BTCUSDT:12345").
        var parts = orderId.Split(':', 2);
        var (symbol, oid) = parts.Length == 2
            ? (parts[0], parts[1])
            : throw new BinanceException("orderId должен быть в формате 'SYMBOL:ORDER_ID'.");

        var ts = GetTimestamp();
        var query = $"symbol={Uri.EscapeDataString(symbol)}" +
                    $"&orderId={Uri.EscapeDataString(oid)}" +
                    $"&timestamp={ts}";
        query += $"&signature={Sign(query)}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{query}");
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/ticker/price?symbol=BTCUSDT
        var response = await httpClient.GetAsync(
            $"/api/v3/ticker/price?symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("price", out var priceEl)
            && double.TryParse(priceEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
                ? price : null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        // GET /api/v3/depth?symbol=BTCUSDT&limit=20
        var response = await httpClient.GetAsync(
            $"/api/v3/depth?symbol={Uri.EscapeDataString(assetId.ToUpperInvariant())}&limit=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        static (double Price, double Qty)[] ParseLevels(JsonElement arr)
        {
            var result = new (double, double)[arr.GetArrayLength()];
            for (int i = 0; i < result.Length; i++)
            {
                var level = arr[i];
                double.TryParse(level[0].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
                double.TryParse(level[1].GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
                result[i] = (p, q);
            }
            return result;
        }

        return new BinanceOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = doc.RootElement.TryGetProperty("bids", out var bids) ? ParseLevels(bids) : [],
            Asks = doc.RootElement.TryGetProperty("asks", out var asks) ? ParseLevels(asks) : []
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
/// Кеш цен Binance с автоматическим обновлением из WebSocket.
/// </summary>
public sealed class BinancePriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, BinancePriceSnapshot> cache = new();
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
            _ => new BinancePriceSnapshot
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

/// <summary>
/// Source-generated JSON контекст для NativeAOT-совместимости.
/// </summary>
[JsonSerializable(typeof(BinancePriceSnapshot))]
[JsonSerializable(typeof(BinancePosition))]
[JsonSerializable(typeof(BinancePosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BinanceJsonContext : JsonSerializerContext;

#endregion
