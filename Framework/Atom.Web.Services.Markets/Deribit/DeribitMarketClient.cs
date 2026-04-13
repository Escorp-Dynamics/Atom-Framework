using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
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
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double? BestBid { get; set; }

    /// <inheritdoc />
    public double? BestAsk { get; set; }

    /// <inheritdoc />
    public double? Midpoint => (BestBid + BestAsk) / 2.0;

    /// <inheritdoc />
    public double? LastTradePrice { get; set; }

    /// <inheritdoc />
    public long LastUpdateTicks { get; set; }
}

/// <summary>Позиция на Deribit.</summary>
public sealed class DeribitPosition : IMarketPosition
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double Quantity { get; set; }

    /// <inheritdoc />
    public double AverageCostBasis { get; set; }

    /// <inheritdoc />
    public double CurrentPrice { get; set; }

    /// <inheritdoc />
    public double MarketValue => Quantity * CurrentPrice;

    /// <inheritdoc />
    public double UnrealizedPnL => MarketValue - Quantity * AverageCostBasis;

    /// <inheritdoc />
    public double UnrealizedPnLPercent =>
        AverageCostBasis != 0 ? (UnrealizedPnL / (Quantity * AverageCostBasis)) * 100 : 0;

    /// <inheritdoc />
    public double RealizedPnL { get; set; }

    /// <inheritdoc />
    public double TotalFees { get; set; }

    /// <inheritdoc />
    public int TradeCount { get; set; }

    /// <inheritdoc />
    public bool IsClosed => Quantity <= 0;
}

/// <summary>Сводка портфеля Deribit.</summary>
public sealed class DeribitPortfolioSummary : IMarketPortfolioSummary
{
    /// <inheritdoc />
    public int OpenPositions { get; init; }

    /// <inheritdoc />
    public int ClosedPositions { get; init; }

    /// <inheritdoc />
    public double TotalMarketValue { get; init; }

    /// <inheritdoc />
    public double TotalCostBasis { get; init; }

    /// <inheritdoc />
    public double TotalUnrealizedPnL { get; init; }

    /// <inheritdoc />
    public double TotalRealizedPnL { get; init; }

    /// <inheritdoc />
    public double TotalFees { get; init; }

    /// <inheritdoc />
    public double NetPnL => TotalUnrealizedPnL + TotalRealizedPnL - TotalFees;
}

/// <summary>Книга ордеров Deribit.</summary>
public sealed class DeribitOrderBookSnapshot : IMarketOrderBookSnapshot
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; }

    /// <inheritdoc />
    public (double Price, double Quantity)[] Bids { get; init; } = [];

    /// <inheritdoc />
    public (double Price, double Quantity)[] Asks { get; init; } = [];
}

/// <summary>Торговый сигнал Deribit.</summary>
public sealed class DeribitTradeSignal : IMarketTradeSignal
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public TradeAction Action { get; init; }

    /// <inheritdoc />
    public double Quantity { get; init; }

    /// <inheritdoc />
    public string? Price { get; init; }

    /// <inheritdoc />
    public double Confidence { get; init; }

    /// <inheritdoc />
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
public class DeribitClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый публичный WebSocket endpoint Deribit API v2.
    /// </summary>
    public const string DefaultWsUrl = "wss://www.deribit.com/ws/api/v2";

    /// <summary>
    /// Создаёт WebSocket-клиент Deribit для рыночных подписок.
    /// </summary>
    /// <param name="reconnectDelay">Задержка перед повторным подключением.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. Ноль означает без ограничения.</param>
    /// <param name="pingInterval">Интервал keepalive ping для runtime.</param>
    public DeribitClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(reconnectDelay: reconnectDelay, maxReconnectAttempts: maxReconnectAttempts, pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "Deribit";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("public/subscribe", 1, marketIds);
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        return BuildCommandMessage("public/unsubscribe", 2, marketIds);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorProperty)
            && errorProperty.ValueKind is JsonValueKind.Object)
        {
            var code = MarketJsonParsingHelpers.TryGetString(errorProperty, "code");
            var message = MarketJsonParsingHelpers.TryGetString(errorProperty, "message") ?? "Unknown Deribit runtime error.";
            await PublishRuntimeErrorAsync(new DeribitException($"Deribit WebSocket error {code}: {message}".Trim())).ConfigureAwait(false);
            return;
        }

        if (root.TryGetProperty("result", out var resultProperty)
            && root.TryGetProperty("id", out var idProperty)
            && MarketJsonParsingHelpers.TryGetString(idProperty) == "1")
        {
            var marketIds = ExtractAcknowledgedMarketIds(resultProperty);
            if (marketIds.Length > 0)
                await PublishSubscriptionAcknowledgedAsync(marketIds, isResubscription: false).ConfigureAwait(false);

            return;
        }

        if (!MarketJsonParsingHelpers.PropertyEquals(root, "method", "subscription")
            || !root.TryGetProperty("params", out var paramsProperty)
            || paramsProperty.ValueKind is not JsonValueKind.Object
            || !paramsProperty.TryGetProperty("channel", out var channelProperty)
            || !paramsProperty.TryGetProperty("data", out var dataProperty)
            || dataProperty.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var channel = MarketJsonParsingHelpers.TryGetString(channelProperty);
        var assetId = string.IsNullOrWhiteSpace(channel) ? null : ExtractMarketId(channel);
        if (string.IsNullOrWhiteSpace(assetId))
        {
            if (string.IsNullOrWhiteSpace(channel)
                || !channel.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase)
                || !channel.EndsWith(".100ms", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            assetId = MarketJsonParsingHelpers.TryGetString(dataProperty, "instrument_name") ?? ExtractMarketId(channel);
        }

        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "best_bid_price");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "best_ask_price");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(dataProperty, "last_price");

        if (!bestBid.HasValue && !bestAsk.HasValue && !lastTrade.HasValue)
            return;

        await PublishMarketUpdateAsync(new MarketRealtimeUpdate(
            assetId,
            bestBid,
            bestAsk,
            lastTrade,
            Environment.TickCount64,
            MarketRealtimeUpdateKind.Ticker)).ConfigureAwait(false);
    }

    private static string[] ExtractAcknowledgedMarketIds(JsonElement resultProperty)
    {
        if (resultProperty.ValueKind is not JsonValueKind.Array)
            return [];

        return resultProperty.EnumerateArray()
            .Select(MarketJsonParsingHelpers.TryGetString)
            .Where(static channel => !string.IsNullOrWhiteSpace(channel) && channel.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase))
            .Select(static channel => ExtractMarketId(channel!))
            .Where(static marketId => !string.IsNullOrWhiteSpace(marketId))
            .Cast<string>()
            .ToArray();
    }

    private static string? ExtractMarketId(JsonElement channelProperty)
    {
        var channel = MarketJsonParsingHelpers.TryGetString(channelProperty);
        return string.IsNullOrWhiteSpace(channel) ? null : ExtractMarketId(channel);
    }

    private static string? ExtractMarketId(string channel)
    {
        if (!channel.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase)
            || !channel.EndsWith(".100ms", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return channel["ticker.".Length..^".100ms".Length];
    }

    private static ReadOnlyMemory<byte> BuildCommandMessage(string method, int id, string[] marketIds)
    {
        using var builder = new Atom.Text.ValueStringBuilder();
        builder.Append("{\"jsonrpc\":\"2.0\",\"id\":");
        builder.Append(id);
        builder.Append(",\"method\":\"");
        builder.Append(method);
        builder.Append("\",\"params\":{\"channels\":[");

        for (var index = 0; index < marketIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append('"');
            builder.Append("ticker.");
            builder.Append(marketIds[index]);
            builder.Append(".100ms\"");
        }

        builder.Append("]}}");
        return Encoding.UTF8.GetBytes(builder.ToString());
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
    /// <summary>
    /// Стандартный базовый URL REST API Deribit v2.
    /// </summary>
    public const string DefaultApiUrl = "https://www.deribit.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly string? clientId;
    private readonly string? clientSecret;
    private readonly IMarketAuthenticator? authenticator;
    private string? accessToken;
    private bool isDisposed;

    /// <summary>
    /// Базовый URL, используемый для REST-запросов.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент Deribit.
    /// </summary>
    /// <remarks>
    /// Deribit использует client_credentials (не HMAC): clientId + clientSecret → /api/v2/public/auth → access_token.
    /// </remarks>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, который будет использован для запросов.</param>
    /// <param name="clientId">Client ID для получения access token.</param>
    /// <param name="clientSecret">Client secret для получения access token.</param>
    /// <param name="authenticator">Опциональный аутентификатор для единообразия API.</param>
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

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        var method = side == TradeSide.Buy ? "private/buy" : "private/sell";
        var orderType = price.HasValue ? "limit" : "market";

        var requestPath = BuildCreateOrderPath(method, assetId, quantity, orderType, price);
        var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
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

    private static string BuildCreateOrderPath(string method, string assetId, double quantity, string orderType, double? price)
    {
        using var sb = new Atom.Text.ValueStringBuilder();
        sb.Append($"/api/v2/{method}?instrument_name={Uri.EscapeDataString(assetId)}");
        sb.Append($"&amount={quantity.ToString("G", CultureInfo.InvariantCulture)}");
        sb.Append($"&type={orderType}");

        if (price.HasValue)
            sb.Append($"&price={price.Value.ToString("G", CultureInfo.InvariantCulture)}");

        return sb.ToString();
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v2/private/cancel?order_id={Uri.EscapeDataString(orderId)}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
public sealed class DeribitPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, DeribitPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт локальный кеш цен Deribit без runtime-подписки.
    /// </summary>
    public DeribitPriceStream()
    {
    }

    /// <summary>
    /// Создаёт кеш цен Deribit и подключает его к runtime-клиенту.
    /// </summary>
    /// <param name="client">WebSocket-клиент Deribit, публикующий обновления рынка.</param>
    public DeribitPriceStream(DeribitClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        runtimeBridge = new MarketRuntimePriceStreamBridge(client, this);
    }

    /// <inheritdoc />
    public int TokenCount => cache.Count;

    /// <inheritdoc />
    public IMarketPriceSnapshot? GetPrice(string assetId) =>
        cache.TryGetValue(assetId, out var snap) ? snap : null;

    /// <inheritdoc />
    public void SetPrice(string assetId, IMarketPriceSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(snapshot);

        cache.AddOrUpdate(assetId,
            _ => new DeribitPriceSnapshot
            {
                AssetId = assetId,
                BestBid = snapshot.BestBid,
                BestAsk = snapshot.BestAsk,
                LastTradePrice = snapshot.LastTradePrice,
                LastUpdateTicks = snapshot.LastUpdateTicks
            },
            (_, existing) =>
            {
                existing.BestBid = snapshot.BestBid;
                existing.BestAsk = snapshot.BestAsk;
                existing.LastTradePrice = snapshot.LastTradePrice;
                existing.LastUpdateTicks = snapshot.LastUpdateTicks;
                return existing;
            });
    }

    /// <summary>
    /// Обновляет кеш на основании нового ticker update от Deribit.
    /// </summary>
    /// <param name="instrument">Идентификатор инструмента.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string instrument, double bid, double ask, double lastTrade)
    {
        SetPrice(instrument, new DeribitPriceSnapshot
        {
            AssetId = instrument,
            BestBid = bid,
            BestAsk = ask,
            LastTradePrice = lastTrade,
            LastUpdateTicks = Environment.TickCount64
        });
    }

    /// <inheritdoc />
    public void ClearCache() => cache.Clear();

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        runtimeBridge?.Dispose();
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
