using System.Buffers;
using System.Net.WebSockets;

namespace Atom.Web.Services.Markets;

/// <summary>
/// Тип нормализованного runtime-обновления рынка.
/// </summary>
public enum MarketRealtimeUpdateKind : byte
{
    /// <summary>Обновление лучшей цены bid/ask.</summary>
    Ticker,

    /// <summary>Обновление последней сделки.</summary>
    Trade,

    /// <summary>Обновление стакана.</summary>
    OrderBook,

    /// <summary>Heartbeat или keepalive.</summary>
    Heartbeat,

    /// <summary>Протокольное подтверждение подписки.</summary>
    SubscriptionAck,

    /// <summary>Ошибка протокола или транспортного слоя.</summary>
    Error
}

/// <summary>
/// Нормализованное runtime-обновление для биржевых клиентов.
/// </summary>
public readonly record struct MarketRealtimeUpdate(
    string AssetId,
    double? BestBid,
    double? BestAsk,
    double? LastTradePrice,
    long LastUpdateTicks,
    MarketRealtimeUpdateKind Kind);

/// <summary>
/// Нормализованный снимок цены, пригодный для записи в общий IWritableMarketPriceStream.
/// </summary>
public sealed class MarketRuntimePriceSnapshot : IMarketPriceSnapshot
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double? BestBid { get; init; }

    /// <inheritdoc />
    public double? BestAsk { get; init; }

    /// <inheritdoc />
    public double? Midpoint => BestBid.HasValue && BestAsk.HasValue ? (BestBid.Value + BestAsk.Value) / 2.0 : null;

    /// <inheritdoc />
    public double? LastTradePrice { get; init; }

    /// <inheritdoc />
    public long LastUpdateTicks { get; init; }
}

/// <summary>
/// Конвертеры нормализованных runtime-обновлений.
/// </summary>
public static class MarketRealtimeUpdateExtensions
{
    /// <summary>
    /// Преобразует runtime update в snapshot для writable price stream.
    /// </summary>
    public static bool TryCreateSnapshot(this MarketRealtimeUpdate update, out MarketRuntimePriceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId)
            || (!update.BestBid.HasValue && !update.BestAsk.HasValue && !update.LastTradePrice.HasValue))
        {
            snapshot = null!;
            return false;
        }

        snapshot = new MarketRuntimePriceSnapshot
        {
            AssetId = update.AssetId,
            BestBid = update.BestBid,
            BestAsk = update.BestAsk,
            LastTradePrice = update.LastTradePrice,
            LastUpdateTicks = update.LastUpdateTicks
        };

        return true;
    }

    /// <summary>
    /// Преобразует runtime update в pipeline update.
    /// </summary>
    public static bool TryCreatePipelineUpdate(this MarketRealtimeUpdate update, out PriceUpdate priceUpdate)
    {
        if (string.IsNullOrWhiteSpace(update.AssetId))
        {
            priceUpdate = default;
            return false;
        }

        var seed = update.LastTradePrice ?? update.BestBid ?? update.BestAsk;
        if (!seed.HasValue)
        {
            priceUpdate = default;
            return false;
        }

        var bid = update.BestBid ?? seed.Value;
        var ask = update.BestAsk ?? seed.Value;
        var last = update.LastTradePrice ?? seed.Value;

        priceUpdate = new PriceUpdate(update.AssetId, bid, ask, last, update.LastUpdateTicks);
        return true;
    }
}

/// <summary>
/// Аргументы события runtime-обновления рынка.
/// </summary>
public sealed class MarketRealtimeUpdateEventArgs(MarketRealtimeUpdate update) : EventArgs
{
    /// <summary>
    /// Нормализованное обновление рынка.
    /// </summary>
    public MarketRealtimeUpdate Update { get; } = update;
}

/// <summary>
/// Аргументы события подтверждения подписки.
/// </summary>
public sealed class MarketSubscriptionEventArgs(string[] marketIds, bool isResubscription) : EventArgs
{
    /// <summary>
    /// Подтверждённые идентификаторы инструментов.
    /// </summary>
    public string[] MarketIds { get; } = marketIds;

    /// <summary>
    /// Является ли подтверждение результатом автоматического переподключения.
    /// </summary>
    public bool IsResubscription { get; } = isResubscription;
}

/// <summary>
/// Аргументы события runtime-ошибки.
/// </summary>
public sealed class MarketRuntimeErrorEventArgs(Exception exception, bool duringReconnect) : EventArgs
{
    /// <summary>
    /// Исключение runtime-слоя.
    /// </summary>
    public Exception Exception { get; } = exception;

    /// <summary>
    /// Возникла ли ошибка во время reconnect-потока.
    /// </summary>
    public bool DuringReconnect { get; } = duringReconnect;
}

/// <summary>
/// Аргументы события успешного переподключения.
/// </summary>
public sealed class MarketReconnectedEventArgs(int attempt, string[] marketIds) : EventArgs
{
    /// <summary>
    /// Номер успешной попытки переподключения.
    /// </summary>
    public int Attempt { get; } = attempt;

    /// <summary>
    /// Подписки, восстановленные после reconnect.
    /// </summary>
    public string[] MarketIds { get; } = marketIds;
}

/// <summary>
/// Базовый runtime-класс для биржевых клиентов реального времени.
/// </summary>
/// <remarks>
/// Отвечает за connect/disconnect, receive loop, ping loop и reconnect/resubscribe.
/// Разбор platform-specific сообщений остаётся в наследнике через <see cref="OnMessageReceivedAsync"/>.
/// </remarks>
public abstract class ExchangeClientBase : IMarketClient, IDisposable
{
    private const int DefaultMaxMessageSize = 1024 * 1024;
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultPingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly HashSet<string> subscribedMarketIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly int maxMessageSize;
    private readonly TimeSpan reconnectDelay;
    private readonly TimeSpan pingInterval;
    private readonly int maxReconnectAttempts;

    private ClientWebSocket? socket;
    private CancellationTokenSource? runtimeCts;
    private Task? receiveLoopTask;
    private Task? pingLoopTask;
    private volatile bool reconnecting;
    private volatile bool isDisposed;

    /// <summary>
    /// Инициализирует базовый runtime-класс.
    /// </summary>
    protected ExchangeClientBase(
        int maxMessageSize = DefaultMaxMessageSize,
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = 0,
        TimeSpan pingInterval = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReconnectAttempts);

        this.maxMessageSize = maxMessageSize;
        this.reconnectDelay = reconnectDelay == default ? DefaultReconnectDelay : reconnectDelay;
        this.maxReconnectAttempts = maxReconnectAttempts;
        this.pingInterval = pingInterval == default ? DefaultPingInterval : pingInterval;
    }

    /// <inheritdoc />
    public abstract string PlatformName { get; }

    /// <summary>
    /// Endpoint подключения конкретной платформы.
    /// </summary>
    protected abstract Uri EndpointUri { get; }

    /// <inheritdoc />
    public bool IsConnected => !isDisposed && socket is not null && IsSocketConnected(socket);

    /// <summary>
    /// Включено ли автоматическое переподключение.
    /// </summary>
    public bool AutoReconnectEnabled { get; set; } = true;

    /// <summary>
    /// Нормализованное runtime-обновление было опубликовано наследником.
    /// </summary>
    public event AsyncEventHandler<ExchangeClientBase, MarketRealtimeUpdateEventArgs>? MarketUpdateReceived;

    /// <summary>
    /// Платформа подтвердила подписку либо восстановление подписок.
    /// </summary>
    public event AsyncEventHandler<ExchangeClientBase, MarketSubscriptionEventArgs>? SubscriptionAcknowledged;

    /// <summary>
    /// В runtime-слое произошла ошибка.
    /// </summary>
    public event AsyncEventHandler<ExchangeClientBase, MarketRuntimeErrorEventArgs>? RuntimeError;

    /// <summary>
    /// Переподключение прошло успешно.
    /// </summary>
    public event AsyncEventHandler<ExchangeClientBase, MarketReconnectedEventArgs>? Reconnected;

    /// <summary>
    /// Текущий набор активных подписок.
    /// </summary>
    protected string[] CurrentSubscriptions
    {
        get
        {
            lock (subscribedMarketIds)
                return [.. subscribedMarketIds];
        }
    }

    /// <inheritdoc />
    public async ValueTask SubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(marketIds);

        var normalizedIds = marketIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedIds.Length == 0)
            return;

        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            string[] newSubscriptions;
            lock (subscribedMarketIds)
            {
                newSubscriptions = normalizedIds.Where(id => subscribedMarketIds.Add(id)).ToArray();
            }

            if (newSubscriptions.Length == 0)
                return;

            if (socket is null)
                throw new InvalidOperationException("WebSocket не инициализирован.");

            await SendSocketMessagesAsync(socket, BuildSubscribeMessages(newSubscriptions), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(marketIds);

        var normalizedIds = marketIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedIds.Length == 0)
            return;

        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket is null || !IsSocketConnected(socket))
                return;

            string[] removedSubscriptions;
            lock (subscribedMarketIds)
            {
                removedSubscriptions = normalizedIds.Where(id => subscribedMarketIds.Remove(id)).ToArray();
            }

            if (removedSubscriptions.Length == 0)
                return;

            await SendSocketMessagesAsync(socket, BuildUnsubscribeMessages(removedSubscriptions), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopRuntimeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    /// <summary>
    /// Строит payload подписки для конкретной платформы.
    /// </summary>
    protected abstract ReadOnlyMemory<byte> BuildSubscribeMessage(string[] marketIds);

    /// <summary>
    /// Строит payload-последовательность подписки для платформ, которым требуется несколько сообщений.
    /// </summary>
    protected virtual IEnumerable<ReadOnlyMemory<byte>> BuildSubscribeMessages(string[] marketIds)
    {
        yield return BuildSubscribeMessage(marketIds);
    }

    /// <summary>
    /// Строит payload отписки для конкретной платформы.
    /// </summary>
    protected abstract ReadOnlyMemory<byte> BuildUnsubscribeMessage(string[] marketIds);

    /// <summary>
    /// Строит payload-последовательность отписки для платформ, которым требуется несколько сообщений.
    /// </summary>
    protected virtual IEnumerable<ReadOnlyMemory<byte>> BuildUnsubscribeMessages(string[] marketIds)
    {
        yield return BuildUnsubscribeMessage(marketIds);
    }

    /// <summary>
    /// Разбирает входящее platform-specific сообщение.
    /// </summary>
    protected abstract ValueTask OnMessageReceivedAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>
    /// Разрешает платформе асинхронно вычислить endpoint подключения до открытия сокета.
    /// </summary>
    protected virtual ValueTask<Uri> ResolveEndpointUriAsync(CancellationToken cancellationToken) => ValueTask.FromResult(EndpointUri);

    /// <summary>
    /// Проверяет, считается ли сокет подключённым.
    /// </summary>
    protected virtual bool IsSocketConnected(ClientWebSocket socket) => socket.State == WebSocketState.Open;

    /// <summary>
    /// Создаёт сокет для подключения.
    /// </summary>
    protected virtual ClientWebSocket CreateSocket() => new();

    /// <summary>
    /// Подключает сокет к вычисленному endpoint платформы.
    /// </summary>
    protected virtual ValueTask ConnectSocketAsync(ClientWebSocket socket, Uri endpointUri, CancellationToken cancellationToken)
        => new(socket.ConnectAsync(endpointUri, cancellationToken));

    /// <summary>
    /// Отправляет payload в сокет.
    /// </summary>
    protected virtual ValueTask SendSocketMessageAsync(
        ClientWebSocket socket,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        SendSocketMessageAsync(socket, payload, WebSocketMessageType.Text, true, cancellationToken);

    /// <summary>
    /// Отправляет payload в сокет с явным типом сообщения.
    /// </summary>
    protected virtual ValueTask SendSocketMessageAsync(
        ClientWebSocket socket,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        socket.SendAsync(payload, messageType, endOfMessage, cancellationToken);

    /// <summary>
    /// Получает очередной фрагмент сообщения из сокета.
    /// </summary>
    protected virtual ValueTask<ValueWebSocketReceiveResult> ReceiveSocketAsync(
        ClientWebSocket socket,
        Memory<byte> buffer,
        CancellationToken cancellationToken) =>
        socket.ReceiveAsync(buffer, cancellationToken);

    /// <summary>
    /// Закрывает сокет.
    /// </summary>
    protected virtual async ValueTask CloseSocketAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Позволяет платформе переопределить ping payload.
    /// </summary>
    protected virtual ReadOnlyMemory<byte> BuildPingMessage() => ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Тип ping-сообщения.
    /// </summary>
    protected virtual WebSocketMessageType PingMessageType => WebSocketMessageType.Binary;

    /// <summary>
    /// Позволяет платформе преобразовать входящий фрейм перед platform-specific разбором.
    /// </summary>
    protected virtual ValueTask<ReadOnlyMemory<byte>?> PrepareIncomingMessageAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        if (messageType != WebSocketMessageType.Text)
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(payload);
    }

    /// <summary>
    /// Вызывается сразу после успешного подключения.
    /// </summary>
    protected virtual ValueTask OnConnectedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Вызывается после закрытия transport runtime.
    /// </summary>
    protected virtual ValueTask OnDisconnectedAsync(Exception? exception, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Публикует нормализованное runtime-обновление.
    /// </summary>
    protected async ValueTask PublishMarketUpdateAsync(MarketRealtimeUpdate update)
    {
        if (MarketUpdateReceived is { } handler)
            await handler(this, new MarketRealtimeUpdateEventArgs(update)).ConfigureAwait(false);
    }

    /// <summary>
    /// Публикует подтверждение подписки.
    /// </summary>
    protected async ValueTask PublishSubscriptionAcknowledgedAsync(string[] marketIds, bool isResubscription)
    {
        if (SubscriptionAcknowledged is { } handler)
            await handler(this, new MarketSubscriptionEventArgs(marketIds, isResubscription)).ConfigureAwait(false);
    }

    /// <summary>
    /// Публикует runtime-ошибку.
    /// </summary>
    protected async ValueTask PublishRuntimeErrorAsync(Exception exception, bool duringReconnect = false)
    {
        if (RuntimeError is { } handler)
            await handler(this, new MarketRuntimeErrorEventArgs(exception, duringReconnect)).ConfigureAwait(false);
    }

    /// <summary>
    /// Публикует событие успешного переподключения.
    /// </summary>
    protected async ValueTask PublishReconnectedAsync(int attempt, string[] marketIds)
    {
        if (Reconnected is { } handler)
            await handler(this, new MarketReconnectedEventArgs(attempt, marketIds)).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет platform-specific runtime-сообщение в текущий активный сокет.
    /// </summary>
    protected async ValueTask SendRuntimeMessageAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken,
        WebSocketMessageType messageType = WebSocketMessageType.Text,
        bool endOfMessage = true)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket is null || !IsSocketConnected(socket))
                throw new InvalidOperationException("WebSocket не подключён.");

            await SendSocketMessageAsync(socket, payload, messageType, endOfMessage, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async ValueTask EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (socket is not null && IsSocketConnected(socket))
            return;

        runtimeCts?.Dispose();
        runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        socket?.Dispose();
        socket = CreateSocket();

        var endpointUri = await ResolveEndpointUriAsync(cancellationToken).ConfigureAwait(false);
        await ConnectSocketAsync(socket, endpointUri, cancellationToken).ConfigureAwait(false);
        await OnConnectedAsync(cancellationToken).ConfigureAwait(false);

        var runtimeToken = runtimeCts.Token;
        receiveLoopTask = Task.Run(() => ReceiveLoopAsync(socket, runtimeToken), CancellationToken.None);

        if (pingInterval > TimeSpan.Zero)
            pingLoopTask = Task.Run(() => PingLoopAsync(socket, runtimeToken), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxMessageSize);
        Exception? disconnectReason = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested && IsSocketConnected(activeSocket))
            {
                var (count, messageType) = await ReceiveFullMessageAsync(activeSocket, buffer, cancellationToken).ConfigureAwait(false);

                if (messageType == WebSocketMessageType.Close)
                    break;

                if (count == 0)
                    continue;

                try
                {
                    var preparedPayload = await PrepareIncomingMessageAsync(
                        buffer.AsMemory(0, count),
                        messageType,
                        cancellationToken).ConfigureAwait(false);

                    if (!preparedPayload.HasValue || preparedPayload.Value.IsEmpty)
                        continue;

                    await OnMessageReceivedAsync(preparedPayload.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await PublishRuntimeErrorAsync(ex).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            disconnectReason = ex;
            await PublishRuntimeErrorAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await OnDisconnectedAsync(disconnectReason, cancellationToken).ConfigureAwait(false);

            if (AutoReconnectEnabled && !isDisposed && !cancellationToken.IsCancellationRequested)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Yield();
                    await ReconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }, CancellationToken.None);
            }
        }
    }

    private async Task PingLoopAsync(ClientWebSocket activeSocket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsSocketConnected(activeSocket))
            {
                await Task.Delay(pingInterval, cancellationToken).ConfigureAwait(false);

                if (!IsSocketConnected(activeSocket))
                    break;

                await activeSocket.SendAsync(BuildPingMessage(), PingMessageType, true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await PublishRuntimeErrorAsync(ex).ConfigureAwait(false);
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (reconnecting)
            return;

        reconnecting = true;
        try
        {
            var subscriptions = CurrentSubscriptions;
            if (subscriptions.Length == 0)
                return;

            var attempt = 0;
            var currentDelay = reconnectDelay;

            while (!isDisposed && !cancellationToken.IsCancellationRequested)
            {
                attempt++;
                if (maxReconnectAttempts > 0 && attempt > maxReconnectAttempts)
                    return;

                try
                {
                    await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);

                    await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (isDisposed)
                            return;

                        await StopRuntimeAsync(cancellationToken).ConfigureAwait(false);
                        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                        if (socket is null)
                            throw new InvalidOperationException("WebSocket не инициализирован после reconnect.");

                        await SendSocketMessagesAsync(socket, BuildSubscribeMessages(subscriptions), cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        connectionLock.Release();
                    }

                    await PublishSubscriptionAcknowledgedAsync(subscriptions, true).ConfigureAwait(false);
                    await PublishReconnectedAsync(attempt, subscriptions).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    await PublishRuntimeErrorAsync(ex, duringReconnect: true).ConfigureAwait(false);
                    currentDelay = TimeSpan.FromTicks(Math.Min(currentDelay.Ticks * 2, MaxReconnectDelay.Ticks));
                }
            }
        }
        finally
        {
            reconnecting = false;
        }
    }

    private async ValueTask<(int Count, WebSocketMessageType MessageType)> ReceiveFullMessageAsync(
        ClientWebSocket activeSocket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalBytes = 0;
        ValueWebSocketReceiveResult result;

        do
        {
            if (totalBytes >= buffer.Length)
                throw new InvalidOperationException("Размер сообщения превышает допустимый предел runtime-клиента.");

            result = await ReceiveSocketAsync(activeSocket, buffer.AsMemory(totalBytes), cancellationToken).ConfigureAwait(false);
            totalBytes += result.Count;
        }
        while (!result.EndOfMessage);

        return (totalBytes, result.MessageType);
    }

    private async ValueTask SendSocketMessagesAsync(
        ClientWebSocket socket,
        IEnumerable<ReadOnlyMemory<byte>> payloads,
        CancellationToken cancellationToken)
    {
        foreach (var payload in payloads)
        {
            if (payload.IsEmpty)
                continue;

            await SendSocketMessageAsync(socket, payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask StopRuntimeAsync(CancellationToken cancellationToken)
    {
        if (runtimeCts is not null)
            await runtimeCts.CancelAsync().ConfigureAwait(false);

        if (socket is not null)
        {
            try
            {
                await CloseSocketAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }

        if (receiveLoopTask is not null)
            await receiveLoopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (pingLoopTask is not null)
            await pingLoopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        runtimeCts?.Dispose();
        runtimeCts = null;

        socket?.Dispose();
        socket = null;

        receiveLoopTask = null;
        pingLoopTask = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        await connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRuntimeAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            connectionLock.Release();
        }

        connectionLock.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
            return;

        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Bridge между runtime events клиента и writable price stream.
/// </summary>
public sealed class MarketRuntimePriceStreamBridge : IDisposable
{
    private readonly ExchangeClientBase client;
    private readonly IWritableMarketPriceStream priceStream;
    private bool isDisposed;

    /// <summary>
    /// Подключает runtime events клиента к writable price stream.
    /// </summary>
    public MarketRuntimePriceStreamBridge(ExchangeClientBase client, IWritableMarketPriceStream priceStream)
    {
        this.client = client;
        this.priceStream = priceStream;
        client.MarketUpdateReceived += OnMarketUpdateReceivedAsync;
    }

    private ValueTask OnMarketUpdateReceivedAsync(ExchangeClientBase sender, MarketRealtimeUpdateEventArgs e)
    {
        if (e.Update.TryCreateSnapshot(out var snapshot))
            priceStream.SetPrice(snapshot.AssetId, snapshot);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        client.MarketUpdateReceived -= OnMarketUpdateReceivedAsync;
    }
}