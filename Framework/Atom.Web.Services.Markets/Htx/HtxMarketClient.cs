using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
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

/// <summary>Позиция на HTX.</summary>
public sealed class HtxPosition : IMarketPosition
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

/// <summary>Сводка портфеля HTX.</summary>
public sealed class HtxPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров HTX.</summary>
public sealed class HtxOrderBookSnapshot : IMarketOrderBookSnapshot
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

/// <summary>Торговый сигнал HTX.</summary>
public sealed class HtxTradeSignal : IMarketTradeSignal
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
public class HtxClient : ExchangeClientBase
{
    /// <summary>
    /// Базовый адрес WebSocket API HTX.
    /// </summary>
    public const string DefaultWsUrl = "wss://api.huobi.pro/ws";

    /// <summary>
    /// Создаёт WebSocket-клиент HTX.
    /// </summary>
    /// <param name="reconnectDelay">Задержка между попытками переподключения.</param>
    /// <param name="maxReconnectAttempts">Максимальное число попыток переподключения. 0 означает без ограничения.</param>
    /// <param name="pingInterval">Интервал внутренних ping-операций runtime.</param>
    public HtxClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(
            reconnectDelay: reconnectDelay,
            maxReconnectAttempts: maxReconnectAttempts,
            pingInterval: pingInterval == default ? TimeSpan.Zero : pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "HTX";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            yield return BuildTopicCommand("sub", marketId);
        }
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId)
            ? ReadOnlyMemory<byte>.Empty
            : BuildTopicCommand("sub", marketId);
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            yield return BuildTopicCommand("unsub", marketId);
        }
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId)
            ? ReadOnlyMemory<byte>.Empty
            : BuildTopicCommand("unsub", marketId);
    }

    /// <inheritdoc />
    protected override async ValueTask<ReadOnlyMemory<byte>?> PrepareIncomingMessageAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        if (messageType is not WebSocketMessageType.Text and not WebSocketMessageType.Binary)
        {
            return null;
        }

        return await TryDecompressPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var status = MarketJsonParsingHelpers.TryGetString(root, "status");

        if (root.TryGetProperty("ping", out var pingProperty))
        {
            var pongMessage = BuildPongMessage(pingProperty);
            await SendRuntimeMessageAsync(pongMessage, cancellationToken, WebSocketMessageType.Text).ConfigureAwait(false);
            return;
        }

        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorCode = MarketJsonParsingHelpers.TryGetString(root, "err-code")
                ?? MarketJsonParsingHelpers.TryGetString(root, "error-code");
            var errorMessage = MarketJsonParsingHelpers.TryGetString(root, "err-msg")
                ?? MarketJsonParsingHelpers.TryGetString(root, "message")
                ?? "Unknown HTX runtime error.";

            await PublishRuntimeErrorAsync(new HtxException($"HTX WebSocket error {errorCode}: {errorMessage}".Trim()))
                .ConfigureAwait(false);
            return;
        }

        if (root.TryGetProperty("subbed", out var subbedProperty)
            && string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var marketId = ExtractMarketId(MarketJsonParsingHelpers.TryGetString(subbedProperty));
            if (!string.IsNullOrWhiteSpace(marketId))
                await PublishSubscriptionAcknowledgedAsync([marketId], isResubscription: false).ConfigureAwait(false);

            return;
        }

        if (!root.TryGetProperty("ch", out var channelProperty)
            || !root.TryGetProperty("tick", out var tickProperty)
            || tickProperty.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var assetId = ExtractMarketId(MarketJsonParsingHelpers.TryGetString(channelProperty));
        if (string.IsNullOrWhiteSpace(assetId))
            return;

        var bestBid = MarketJsonParsingHelpers.TryParseDouble(tickProperty, "bid");
        var bestAsk = MarketJsonParsingHelpers.TryParseDouble(tickProperty, "ask");
        var lastTrade = MarketJsonParsingHelpers.TryParseDouble(tickProperty, "lastPrice")
            ?? MarketJsonParsingHelpers.TryParseDouble(tickProperty, "close");

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

    private static ReadOnlyMemory<byte> BuildTopicCommand(string command, string marketId)
    {
        var normalizedMarketId = marketId.ToLowerInvariant();
        return Encoding.UTF8.GetBytes($"{{\"{command}\":\"market.{normalizedMarketId}.ticker\",\"id\":\"{normalizedMarketId}\"}}");
    }

    private static string? ExtractMarketId(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)
            || !topic.StartsWith("market.", StringComparison.OrdinalIgnoreCase)
            || !topic.EndsWith(".ticker", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var start = "market.".Length;
        var length = topic.Length - start - ".ticker".Length;
        return length > 0 ? topic.Substring(start, length) : null;
    }

    private static ReadOnlyMemory<byte> BuildPongMessage(JsonElement pingProperty)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WritePropertyName("pong");
        pingProperty.WriteTo(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static async ValueTask<ReadOnlyMemory<byte>> TryDecompressPayloadAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty)
            return payload;

        if (LooksLikeJson(payload.Span))
            return payload.ToArray();

        using var compressedStream = new MemoryStream(payload.ToArray(), writable: false);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        await gzipStream.CopyToAsync(decompressedStream, cancellationToken).ConfigureAwait(false);
        return decompressedStream.ToArray();
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> payload)
    {
        foreach (var value in payload)
        {
            if (value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
                continue;

            return value is (byte)'{' or (byte)'[';
        }

        return false;
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
    /// <summary>
    /// Базовый адрес REST API HTX.
    /// </summary>
    public const string DefaultApiUrl = "https://api.huobi.pro";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <summary>
    /// Базовый URL REST API, используемый клиентом.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент HTX.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, который будет использоваться для запросов.</param>
    /// <param name="apiKey">API-ключ HTX.</param>
    /// <param name="apiSecret">Секретный ключ HTX.</param>
    /// <param name="authenticator">Готовый аутентификатор подписанных запросов.</param>
    public HtxRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Htx(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        // 1) Получаем account-id (упрощённо — в реальном коде кешируется)
        if (authenticator is null)
            throw new HtxException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var accountId = await GetSpotAccountIdAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new HtxException("Не удалось получить account-id.");

        // 2) Формируем подписанный запрос
        const string path = "/v1/order/orders/place";

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

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        authenticator.SignRequest(request, bodyJson);

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

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new HtxException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var path = $"/v1/order/orders/{Uri.EscapeDataString(orderId)}/submitcancel";

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        authenticator.SignRequest(request);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Получает ID полне спот-аккаунта.</summary>
    private async ValueTask<string?> GetSpotAccountIdAsync(CancellationToken cancellationToken)
    {
        const string path = "/v1/account/accounts";

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        authenticator!.SignRequest(request);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Освобождает ресурсы REST-клиента.
    /// </summary>
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
public sealed class HtxPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, HtxPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>
    /// Создаёт пустой поток цен HTX.
    /// </summary>
    public HtxPriceStream()
    {
    }

    /// <summary>
    /// Создаёт поток цен HTX и связывает его с runtime-клиентом.
    /// </summary>
    /// <param name="client">Клиент, публикующий обновления рынка.</param>
    public HtxPriceStream(HtxClient client)
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
            _ => new HtxPriceSnapshot
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
    /// Обновляет кеш цен из runtime-обновления HTX.
    /// </summary>
    /// <param name="symbol">Идентификатор инструмента.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new HtxPriceSnapshot
        {
            AssetId = symbol,
            BestBid = bid,
            BestAsk = ask,
            LastTradePrice = lastTrade,
            LastUpdateTicks = Environment.TickCount64
        });
    }

    /// <inheritdoc />
    public void ClearCache() => cache.Clear();

    /// <summary>
    /// Освобождает ресурсы потока цен и очищает кеш.
    /// </summary>
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

[JsonSerializable(typeof(HtxPriceSnapshot))]
[JsonSerializable(typeof(HtxPosition))]
[JsonSerializable(typeof(HtxPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class HtxJsonContext : JsonSerializerContext;

#endregion
