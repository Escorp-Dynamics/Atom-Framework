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

/// <summary>Позиция на MEXC.</summary>
public sealed class MexcPosition : IMarketPosition
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

/// <summary>Сводка портфеля MEXC.</summary>
public sealed class MexcPortfolioSummary : IMarketPortfolioSummary
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

/// <summary>Книга ордеров MEXC.</summary>
public sealed class MexcOrderBookSnapshot : IMarketOrderBookSnapshot
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

/// <summary>Торговый сигнал MEXC.</summary>
public sealed class MexcTradeSignal : IMarketTradeSignal
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

/// <summary>Исключение операций MEXC.</summary>
public sealed class MexcException(string message, Exception? innerException = null)
    : MarketException(message, innerException ?? new Exception(message));

#endregion

#region IMarketClient — WebSocket

/// <summary>
/// WebSocket-клиент MEXC для рыночных данных.
/// </summary>
/// <remarks>
/// WebSocket: wss://wbs-api.mexc.com/ws.
/// Control-plane использует JSON-команды, market data для pb-каналов приходит в protobuf-бинарном формате.
/// </remarks>
public class MexcClient : ExchangeClientBase
{
    /// <summary>Базовый WebSocket endpoint MEXC market runtime.</summary>
    public const string DefaultWsUrl = "wss://wbs-api.mexc.com/ws";
    private const string BookTickerInterval = "100ms";
    private const string MiniTickerTimezone = "UTC+0";

    /// <summary>
    /// Создаёт runtime-клиент MEXC с поддержкой reconnect и keepalive.
    /// </summary>
    /// <param name="reconnectDelay">Базовая задержка между попытками reconnect.</param>
    /// <param name="maxReconnectAttempts">Максимум попыток reconnect. Ноль означает без лимита.</param>
    /// <param name="pingInterval">Интервал отправки клиентского PING.</param>
    public MexcClient(
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
        : base(
            reconnectDelay: reconnectDelay,
            maxReconnectAttempts: maxReconnectAttempts,
            pingInterval: pingInterval)
    {
    }

    /// <inheritdoc />
    public override string PlatformName => "MEXC";

    /// <inheritdoc />
    protected override Uri EndpointUri => new(DefaultWsUrl);

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            yield return BuildCommand("SUBSCRIPTION", BuildBookTickerChannel(marketId));
            yield return BuildCommand("SUBSCRIPTION", BuildMiniTickerChannel(marketId));
        }
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId)
            ? ReadOnlyMemory<byte>.Empty
            : BuildCommand("SUBSCRIPTION", BuildBookTickerChannel(marketId));
    }

    /// <inheritdoc />
    protected override IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
    {
        foreach (var marketId in marketIds)
        {
            yield return BuildCommand("UNSUBSCRIPTION", BuildBookTickerChannel(marketId));
            yield return BuildCommand("UNSUBSCRIPTION", BuildMiniTickerChannel(marketId));
        }
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds)
    {
        var marketId = marketIds.FirstOrDefault();
        return string.IsNullOrWhiteSpace(marketId)
            ? ReadOnlyMemory<byte>.Empty
            : BuildCommand("UNSUBSCRIPTION", BuildBookTickerChannel(marketId));
    }

    /// <inheritdoc />
    protected override WebSocketMessageType PingMessageType => WebSocketMessageType.Text;

    /// <inheritdoc />
    protected override ReadOnlyMemory<byte> BuildPingMessage() => Encoding.UTF8.GetBytes("{\"method\":\"PING\"}");

    /// <inheritdoc />
    protected override ValueTask<ReadOnlyMemory<byte>?> PrepareIncomingMessageAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        if (messageType is not WebSocketMessageType.Text and not WebSocketMessageType.Binary)
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(payload.ToArray());
    }

    /// <inheritdoc />
    protected override async ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (LooksLikeJson(payload.Span))
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var code = MarketJsonParsingHelpers.TryParseInt32(root, "code");
            var message = MarketJsonParsingHelpers.TryGetString(root, "msg");

            if (string.Equals(message, "PONG", StringComparison.OrdinalIgnoreCase))
                return;

            if (code is 0 && TryExtractMarketId(message, out var acknowledgedMarketId))
            {
                await PublishSubscriptionAcknowledgedAsync([acknowledgedMarketId], isResubscription: false).ConfigureAwait(false);
                return;
            }

            if (code.HasValue && code.Value != 0)
            {
                await PublishRuntimeErrorAsync(new MexcException($"MEXC WebSocket error {code}: {message ?? "Unknown MEXC runtime error."}"))
                    .ConfigureAwait(false);
            }

            return;
        }

        if (!TryParseRealtimeUpdate(payload.Span, out var update))
            return;

        await PublishMarketUpdateAsync(update).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> BuildCommand(string method, string channel) =>
        Encoding.UTF8.GetBytes($"{{\"method\":\"{method}\",\"params\":[\"{channel}\"]}}");

    private static string BuildBookTickerChannel(string marketId) =>
        $"spot@public.aggre.bookTicker.v3.api.pb@{BookTickerInterval}@{marketId.ToUpperInvariant()}";

    private static string BuildMiniTickerChannel(string marketId) =>
        $"spot@public.miniTicker.v3.api.pb@{marketId.ToUpperInvariant()}@{MiniTickerTimezone}";

    private static bool TryExtractMarketId(string? channel, out string marketId)
    {
        marketId = string.Empty;
        if (string.IsNullOrWhiteSpace(channel))
            return false;

        var parts = channel.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        if (channel.StartsWith("spot@public.aggre.bookTicker.v3.api.pb@", StringComparison.OrdinalIgnoreCase))
        {
            marketId = parts[^1].ToLowerInvariant();
            return true;
        }

        if (channel.StartsWith("spot@public.miniTicker.v3.api.pb@", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 2)
        {
            marketId = parts[^2].ToLowerInvariant();
            return true;
        }

        return false;
    }

    private static bool TryParseRealtimeUpdate(ReadOnlySpan<byte> payload, out MarketRealtimeUpdate update)
    {
        update = default;
        if (!MexcProtoReader.TryReadWrapper(payload, out var channel, out var symbol, out var sendTime, out var bodyFieldNumber, out var bodyPayload))
            return false;

        symbol = string.IsNullOrWhiteSpace(symbol) && TryExtractMarketId(channel, out var extractedMarketId)
            ? extractedMarketId
            : symbol?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        var lastUpdateTicks = sendTime ?? Environment.TickCount64;

        if (bodyFieldNumber == MexcProtoReader.PublicAggreBookTickerFieldNumber
            && MexcProtoReader.TryReadAggreBookTicker(bodyPayload, out var bestBid, out var bestAsk))
        {
            update = new MarketRealtimeUpdate(symbol, bestBid, bestAsk, null, lastUpdateTicks, MarketRealtimeUpdateKind.Ticker);
            return true;
        }

        if (bodyFieldNumber == MexcProtoReader.PublicMiniTickerFieldNumber
            && MexcProtoReader.TryReadMiniTicker(bodyPayload, out var lastTradePrice))
        {
            update = new MarketRealtimeUpdate(symbol, null, null, lastTradePrice, lastUpdateTicks, MarketRealtimeUpdateKind.Ticker);
            return true;
        }

        return false;
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

    private static class MexcProtoReader
    {
        public const int PublicMiniTickerFieldNumber = 309;
        public const int PublicAggreBookTickerFieldNumber = 315;

        public static bool TryReadWrapper(
            ReadOnlySpan<byte> payload,
            out string? channel,
            out string? symbol,
            out long? sendTime,
            out int bodyFieldNumber,
            out byte[]? bodyPayload)
        {
            channel = null;
            symbol = null;
            sendTime = null;
            bodyFieldNumber = 0;
            bodyPayload = null;

            var offset = 0;
            while (TryReadFieldHeader(payload, ref offset, out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == 2:
                        if (!TryReadString(payload, ref offset, out channel))
                            return false;
                        break;
                    case 3 when wireType == 2:
                        if (!TryReadString(payload, ref offset, out symbol))
                            return false;
                        break;
                    case 6 when wireType == 0:
                        if (!TryReadVarint(payload, ref offset, out var sendTimeValue))
                            return false;
                        sendTime = (long)sendTimeValue;
                        break;
                    case PublicMiniTickerFieldNumber or PublicAggreBookTickerFieldNumber when wireType == 2:
                        if (!TryReadLengthDelimited(payload, ref offset, out bodyPayload))
                            return false;
                        bodyFieldNumber = fieldNumber;
                        break;
                    default:
                        if (!SkipField(payload, ref offset, wireType))
                            return false;
                        break;
                }
            }

            return !string.IsNullOrWhiteSpace(channel) && bodyFieldNumber != 0 && bodyPayload is { Length: > 0 };
        }

        public static bool TryReadAggreBookTicker(ReadOnlySpan<byte> payload, out double? bestBid, out double? bestAsk)
        {
            bestBid = null;
            bestAsk = null;

            var offset = 0;
            while (TryReadFieldHeader(payload, ref offset, out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == 2:
                        if (!TryReadStringAsDouble(payload, ref offset, out bestBid))
                            return false;
                        break;
                    case 3 when wireType == 2:
                        if (!TryReadStringAsDouble(payload, ref offset, out bestAsk))
                            return false;
                        break;
                    default:
                        if (!SkipField(payload, ref offset, wireType))
                            return false;
                        break;
                }
            }

            return bestBid.HasValue || bestAsk.HasValue;
        }

        public static bool TryReadMiniTicker(ReadOnlySpan<byte> payload, out double? lastTradePrice)
        {
            lastTradePrice = null;

            var offset = 0;
            while (TryReadFieldHeader(payload, ref offset, out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 2 when wireType == 2:
                        return TryReadStringAsDouble(payload, ref offset, out lastTradePrice) && lastTradePrice.HasValue;
                    default:
                        if (!SkipField(payload, ref offset, wireType))
                            return false;
                        break;
                }
            }

            return false;
        }

        private static bool TryReadFieldHeader(ReadOnlySpan<byte> payload, ref int offset, out int fieldNumber, out int wireType)
        {
            fieldNumber = 0;
            wireType = 0;
            if (offset >= payload.Length)
                return false;

            if (!TryReadVarint(payload, ref offset, out var key))
                return false;

            fieldNumber = (int)(key >> 3);
            wireType = (int)(key & 0x07);
            return fieldNumber > 0;
        }

        private static bool TryReadString(ReadOnlySpan<byte> payload, ref int offset, out string? value)
        {
            value = null;
            if (!TryReadLengthDelimited(payload, ref offset, out var bytes))
                return false;

            if (bytes is null)
                return false;

            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        private static bool TryReadStringAsDouble(ReadOnlySpan<byte> payload, ref int offset, out double? value)
        {
            value = null;
            if (!TryReadString(payload, ref offset, out var stringValue))
                return false;

            value = string.IsNullOrWhiteSpace(stringValue)
                ? null
                : double.Parse(stringValue, CultureInfo.InvariantCulture);
            return true;
        }

        private static bool TryReadLengthDelimited(ReadOnlySpan<byte> payload, ref int offset, out byte[]? value)
        {
            value = null;
            if (!TryReadVarint(payload, ref offset, out var lengthValue))
                return false;

            var length = checked((int)lengthValue);
            if (length < 0 || offset + length > payload.Length)
                return false;

            value = payload.Slice(offset, length).ToArray();
            offset += length;
            return true;
        }

        private static bool TryReadVarint(ReadOnlySpan<byte> payload, ref int offset, out ulong value)
        {
            value = 0;
            var shift = 0;

            while (offset < payload.Length && shift < 64)
            {
                var current = payload[offset++];
                value |= (ulong)(current & 0x7Fu) << shift;
                if ((current & 0x80) == 0)
                    return true;

                shift += 7;
            }

            return false;
        }

        private static bool SkipField(ReadOnlySpan<byte> payload, ref int offset, int wireType)
        {
            switch (wireType)
            {
                case 0:
                    return TryReadVarint(payload, ref offset, out _);
                case 1:
                    if (offset + 8 > payload.Length)
                        return false;

                    offset += 8;
                    return true;
                case 2:
                    return TryReadLengthDelimited(payload, ref offset, out _);
                case 5:
                    if (offset + 4 > payload.Length)
                        return false;

                    offset += 4;
                    return true;
                default:
                    return false;
            }
        }
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
    /// <summary>Базовый REST endpoint MEXC Spot API.</summary>
    public const string DefaultApiUrl = "https://api.mexc.com";

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly IMarketAuthenticator? authenticator;
    private bool isDisposed;

    /// <inheritdoc />
    public string BaseUrl { get; }

    /// <summary>Аутентификатор запросов (унифицированный HMAC).</summary>
    public IMarketAuthenticator? Authenticator => authenticator;

    /// <summary>
    /// Создаёт REST-клиент MEXC.
    /// </summary>
    /// <param name="baseUrl">Базовый URL API.</param>
    /// <param name="httpClient">HTTP-клиент, если нужен внешний lifecycle.</param>
    /// <param name="apiKey">API-ключ для подписанных запросов.</param>
    /// <param name="apiSecret">Секрет для HMAC-SHA256 подписи.</param>
    /// <param name="authenticator">Готовый аутентификатор для подписанных запросов.</param>
    public MexcRestClient(string baseUrl = DefaultApiUrl, HttpClient? httpClient = null,
        string? apiKey = null, string? apiSecret = null, IMarketAuthenticator? authenticator = null)
    {
        BaseUrl = baseUrl;
        this.httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri(baseUrl) };
        disposeHttpClient = httpClient is null;
        this.authenticator = authenticator
            ?? (apiKey is not null && apiSecret is not null
                ? MarketAuthenticators.Mexc(apiKey, apiSecret) : null);
    }

    /// <inheritdoc />
    public async ValueTask<string?> CreateOrderAsync(
        string assetId, TradeSide side, double quantity, double? price = null,
        CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new MexcException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        var sideStr = side == TradeSide.Buy ? "BUY" : "SELL";
        var orderType = price.HasValue ? "LIMIT" : "MARKET";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

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
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v3/order?{queryString}");
        authenticator.SignRequest(request, queryString);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MexcException($"CreateOrder failed ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("orderId", out var orderId) ? MarketJsonParsingHelpers.TryGetString(orderId) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (authenticator is null)
            throw new MexcException("Аутентификация не настроена. Передайте apiKey/apiSecret или IMarketAuthenticator.");

        // Формат orderId: "SYMBOL:ORDER_ID"
        var parts = orderId.Split(':', 2);
        if (parts.Length != 2)
            throw new MexcException($"orderId должен быть в формате SYMBOL:ORDER_ID, получено: {orderId}");

        var symbol = parts[0];
        var oid = parts[1];
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var queryString = $"symbol={Uri.EscapeDataString(symbol)}&orderId={Uri.EscapeDataString(oid)}&timestamp={ts}";

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{queryString}");
        authenticator.SignRequest(request, queryString);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async ValueTask<double?> GetPriceAsync(string assetId, TradeSide side, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            $"/api/v3/ticker/price?symbol={Uri.EscapeDataString(assetId)}",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return MarketJsonParsingHelpers.TryParseDouble(doc.RootElement, "price");
    }

    /// <inheritdoc />
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
                list[i] = (
                    MarketJsonParsingHelpers.TryParseDouble(level[0]) ?? 0,
                    MarketJsonParsingHelpers.TryParseDouble(level[1]) ?? 0);
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

/// <summary>Кеш цен MEXC.</summary>
public sealed class MexcPriceStream : IWritableMarketPriceStream
{
    private readonly ConcurrentDictionary<string, MexcPriceSnapshot> cache = new();
    private readonly MarketRuntimePriceStreamBridge? runtimeBridge;
    private bool isDisposed;

    /// <summary>Создаёт автономный локальный кеш цен без runtime bridge.</summary>
    public MexcPriceStream()
    {
    }

    /// <summary>Создаёт кеш цен и подключает его к runtime bridge клиента MEXC.</summary>
    /// <param name="client">Источник runtime-обновлений рынка.</param>
    public MexcPriceStream(MexcClient client)
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
            _ => new MexcPriceSnapshot
            {
                AssetId = assetId,
                BestBid = snapshot.BestBid,
                BestAsk = snapshot.BestAsk,
                LastTradePrice = snapshot.LastTradePrice,
                LastUpdateTicks = snapshot.LastUpdateTicks
            },
            (_, existing) =>
            {
                existing.BestBid = snapshot.BestBid ?? existing.BestBid;
                existing.BestAsk = snapshot.BestAsk ?? existing.BestAsk;
                existing.LastTradePrice = snapshot.LastTradePrice ?? existing.LastTradePrice;
                existing.LastUpdateTicks = snapshot.LastUpdateTicks;
                return existing;
            });
    }

    /// <summary>Обновляет локальный кеш цен из готового набора bid, ask и last trade.</summary>
    /// <param name="symbol">Идентификатор торговой пары.</param>
    /// <param name="bid">Лучшая цена покупки.</param>
    /// <param name="ask">Лучшая цена продажи.</param>
    /// <param name="lastTrade">Цена последней сделки.</param>
    public void UpdatePrice(string symbol, double bid, double ask, double lastTrade)
    {
        SetPrice(symbol, new MexcPriceSnapshot
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

[JsonSerializable(typeof(MexcPriceSnapshot))]
[JsonSerializable(typeof(MexcPosition))]
[JsonSerializable(typeof(MexcPosition[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class MexcJsonContext : JsonSerializerContext;

#endregion
