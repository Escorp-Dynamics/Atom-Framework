using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Deribit;

// ═══════════════════════════════════════════════════════════════════
// Реализация Markets/ для Deribit (деривативы: фьючерсы + опционы).
// WebSocket (wss://www.deribit.com/ws/api/v2) + REST API v2.
// Аутентификация: client_credentials через API.
// ═══════════════════════════════════════════════════════════════════

#region Модели

/// <summary>Снимок цены Deribit.</summary>
public sealed class DeribitPriceSnapshot : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на Deribit.</summary>
public sealed class DeribitPosition : IMarketPosition
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

/// <summary>Сводка портфеля Deribit.</summary>
public sealed class DeribitPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров Deribit.</summary>
public sealed class DeribitOrderBookSnapshot : IMarketOrderBookSnapshot
{
    public required string AssetId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public (double Price, double Quantity)[] Bids { get; init; } = [];
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал Deribit.</summary>
public sealed class DeribitTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций Deribit.</summary>
public sealed class DeribitException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент Deribit для рыночных данных и RPC.
/// </summary>
/// <remarks>
/// Deribit использует JSON-RPC 2.0 через WebSocket.
/// Подписка: {"jsonrpc":"2.0","method":"public/subscribe","params":{"channels":["ticker.BTC-PERPETUAL.100ms"]}}.
/// Аутентификация через WS: public/auth с client_credentials.
/// </remarks>
public sealed class DeribitClient : IMarketClient, IDisposable
{
    public const string DefaultWsUrl = "wss://www.deribit.com/ws/api/v2";

    private ClientWebSocket? socket;
    private CancellationTokenSource? cts;
    private volatile bool isDisposed;

    public string PlatformName => "Deribit";
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

        // Deribit JSON-RPC: subscribe на ticker-каналы
        var channels = marketIds.Select(s => $"ticker.{s}.100ms").ToArray();
        var rpc = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "public/subscribe",
            @params = new { channels }
        });

        var bytes = Encoding.UTF8.GetBytes(rpc);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        if (socket?.State != WebSocketState.Open) return;

        var channels = marketIds.Select(s => $"ticker.{s}.100ms").ToArray();
        var rpc = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "public/unsubscribe",
            @params = new { channels }
        });

        var bytes = Encoding.UTF8.GetBytes(rpc);
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

#region IMarketRestClient — REST API v2

/// <summary>
/// REST-клиент Deribit API v2.
/// </summary>
/// <remarks>
/// Deribit аутентификация: client_credentials → access_token → Bearer header.
/// POST /api/v2/private/buy, /api/v2/private/sell, /api/v2/private/cancel.
/// GET /api/v2/public/ticker, /api/v2/public/get_order_book.
/// </remarks>
public sealed class DeribitRestClient : IMarketRestClient, IDisposable
{
    public const string DefaultApiUrl = "https://www.deribit.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string? clientId;
    private readonly string? clientSecret;
    private readonly IMarketAuthenticator? authenticator;
    private string? accessToken;
    private bool isDisposed;

    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Deribit.
    /// </summary>
    /// <remarks>
    /// Deribit использует client_credentials (не HMAC): clientId + clientSecret → /api/v2/public/auth → access_token.
    /// </remarks>
    public DeribitRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? clientId = null, string? clientSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.clientId = clientId;
        this.clientSecret = clientSecret;
        this.authenticator = authenticator;
    }

    /// <summary>Получает access_token через client_credentials.</summary>
    private async ValueTask EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (accessToken is not null) return;
        if (clientId is null || clientSecret is null)
            throw new DeribitException("clientId/clientSecret не заданы.");

        var response = await httpClient.GetAsync(
            $"/api/v2/public/auth?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new DeribitException($"Auth failed ({response.StatusCode})");

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        accessToken = doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("access_token", out var token)
            ? token.GetString() : throw new DeribitException("No access_token in response");
    }

    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        var method = side == TradeSide.Buy ? "private/buy" : "private/sell";
        var orderType = price.HasValue ? "limit" : "market";

        var sb = new StringBuilder();
        sb.Append($"/api/v2/{method}?instrument_name={Uri.EscapeDataString(assetId)}");
        sb.Append($"&amount={quantity.ToString("G", CultureInfo.InvariantCulture)}");
        sb.Append($"&type={orderType}");

        if (price.HasValue)
            sb.Append($"&price={price.Value.ToString("G", CultureInfo.InvariantCulture)}");

        var request = new HttpRequestMessage(HttpMethod.Get, sb.ToString());
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new DeribitException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("order", out var order)
            && order.TryGetProperty("order_id", out var orderId)
            ? orderId.GetString() : null;
    }

    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v2/private/cancel?order_id={Uri.EscapeDataString(orderId)}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v2/public/ticker?instrument_name={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

        var prop = side == TradeSide.Buy ? "best_ask_price" : "best_bid_price";
        return result.TryGetProperty(prop, out var p) ? p.GetDouble() : null;
    }

    public async ValueTask<IMarketOrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v2/public/get_order_book?instrument_name={Uri.EscapeDataString(assetId)}&depth=20",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

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

        return new DeribitOrderBookSnapshot
        {
            AssetId = assetId,
            Timestamp = DateTimeOffset.UtcNow,
            Bids = ParseLevels(result, "bids"),
            Asks = ParseLevels(result, "asks")
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

/// <summary>Кеш цен Deribit.</summary>
public sealed class DeribitPriceStream : IMarketPriceStream
{
    private readonly ConcurrentDictionary<string, DeribitPriceSnapshot> cache = new();
    private bool isDisposed;

    public int TokenCount => cache.Count;

    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    public void UpdatePrice(string instrument, double bid, double ask, double lastTrade)
    {
        cache.AddOrUpdate(instrument,
            _ => new DeribitPriceSnapshot
            {
                AssetId = instrument, BestBid = bid, BestAsk = ask,
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

[JsonSerializable(typeof(DeribitPriceSnapshot))]
[JsonSerializable(typeof(DeribitPosition))]
[JsonSerializable(typeof(DeribitPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class DeribitJsonContext : JsonSerializerContext;

#endregion
