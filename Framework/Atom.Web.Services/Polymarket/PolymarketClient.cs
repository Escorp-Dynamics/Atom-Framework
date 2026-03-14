using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Клиент для взаимодействия с WebSocket API Polymarket CLOB.
/// Поддерживает подписку на рыночные данные (стакан, цены, сделки)
/// и пользовательские данные (ордера, трейды) через аутентифицированный канал.
/// </summary>
/// <remarks>
/// Полностью совместим с NativeAOT. Использует source-generated JSON-сериализацию.
/// Поддерживает автоматическое переподключение с экспоненциальным backoff и ping/pong keepalive.
/// </remarks>
public sealed class PolymarketClient : IMarketClient, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Базовый URL WebSocket API Polymarket CLOB.
    /// </summary>
    public const string DefaultBaseUrl = "wss://ws-subscriptions-clob.polymarket.com/ws";

    /// <summary>
    /// Максимальный размер входящего сообщения по умолчанию (1 МБ).
    /// </summary>
    private const int DefaultMaxMessageSize = 1024 * 1024;

    /// <summary>
    /// Интервал переподключения по умолчанию.
    /// </summary>
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Максимальный интервал переподключения при экспоненциальном backoff (5 минут).
    /// </summary>
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Максимальное количество попыток переподключения (0 = безлимитно).
    /// </summary>
    private const int DefaultMaxReconnectAttempts = 0;

    /// <summary>
    /// Интервал отправки ping-кадров по умолчанию (30 секунд).
    /// </summary>
    private static readonly TimeSpan DefaultPingInterval = TimeSpan.FromSeconds(30);

    private readonly string baseUrl;
    private readonly int maxMessageSize;
    private readonly TimeSpan reconnectDelay;
    private readonly int maxReconnectAttempts;
    private readonly TimeSpan pingInterval;
    private readonly SemaphoreSlim marketLock = new(1, 1);
    private readonly SemaphoreSlim userLock = new(1, 1);

    private ClientWebSocket? marketSocket;
    private ClientWebSocket? userSocket;
    private CancellationTokenSource? marketCts;
    private CancellationTokenSource? userCts;
    private Task? marketReceiveLoop;
    private Task? userReceiveLoop;
    private Task? marketPingLoop;
    private Task? userPingLoop;
    private PolymarketAuth? auth;
    private volatile bool isDisposed;

    // Данные для автоматического переподключения
    private string[]? lastMarketSubscriptionMarkets;
    private string[]? lastMarketSubscriptionAssets;
    private string[]? lastUserSubscriptionMarkets;
    private string[]? lastUserSubscriptionAssets;
    private volatile bool autoReconnectEnabled = true;

    /// <summary>
    /// Происходит при получении снимка стакана ордеров.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketBookEventArgs>? BookSnapshotReceived;

    /// <summary>
    /// Происходит при изменении уровней цены в стакане.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketPriceChangeEventArgs>? PriceChanged;

    /// <summary>
    /// Происходит при обновлении цены последней сделки.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketLastTradePriceEventArgs>? LastTradePriceReceived;

    /// <summary>
    /// Происходит при изменении минимального шага цены.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketTickSizeChangeEventArgs>? TickSizeChanged;

    /// <summary>
    /// Происходит при обновлении ордера пользователя (канал user).
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketOrderEventArgs>? OrderUpdated;

    /// <summary>
    /// Происходит при исполнении сделки пользователя (канал user).
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketTradeEventArgs>? TradeReceived;

    /// <summary>
    /// Происходит при разрыве соединения с рыночным каналом.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketDisconnectedEventArgs>? MarketDisconnected;

    /// <summary>
    /// Происходит при разрыве соединения с каналом пользователя.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketDisconnectedEventArgs>? UserDisconnected;

    /// <summary>
    /// Происходит при ошибке обработки входящего сообщения.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Происходит при успешном автоматическом переподключении к каналу.
    /// </summary>
    public event AsyncEventHandler<PolymarketClient, PolymarketReconnectedEventArgs>? Reconnected;

    /// <summary>
    /// Определяет, подключён ли клиент к рыночному каналу.
    /// </summary>
    public bool IsMarketConnected => !isDisposed && marketSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Определяет, подключён ли клиент к каналу пользователя.
    /// </summary>
    public bool IsUserConnected => !isDisposed && userSocket?.State == WebSocketState.Open;

    // IMarketClient — явная реализация
    string IMarketClient.PlatformName => "Polymarket";
    bool IMarketClient.IsConnected => IsMarketConnected || IsUserConnected;
    ValueTask IMarketClient.SubscribeAsync(string[] marketIds, CancellationToken cancellationToken) =>
        SubscribeMarketAsync(markets: marketIds, cancellationToken: cancellationToken);
    ValueTask IMarketClient.UnsubscribeAsync(string[] marketIds, CancellationToken cancellationToken) =>
        UnsubscribeMarketAsync(markets: marketIds, cancellationToken: cancellationToken);
    ValueTask IMarketClient.DisconnectAsync(CancellationToken cancellationToken) =>
        DisconnectMarketAsync(cancellationToken);

    /// <summary>
    /// Определяет, включено ли автоматическое переподключение.
    /// </summary>
    public bool AutoReconnectEnabled
    {
        get => autoReconnectEnabled;
        set => autoReconnectEnabled = value;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketClient"/> с настройками по умолчанию.
    /// </summary>
    public PolymarketClient() : this(DefaultBaseUrl) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PolymarketClient"/> с указанным базовым URL.
    /// </summary>
    /// <param name="baseUrl">Базовый URL WebSocket-сервера Polymarket.</param>
    /// <param name="maxMessageSize">Максимальный размер входящего сообщения в байтах.</param>
    /// <param name="reconnectDelay">Начальная задержка перед попыткой переподключения.</param>
    /// <param name="maxReconnectAttempts">Максимальное количество попыток переподключения (0 = безлимитно).</param>
    /// <param name="pingInterval">Интервал отправки ping-кадров для поддержания соединения.</param>
    public PolymarketClient(
        string baseUrl,
        int maxMessageSize = DefaultMaxMessageSize,
        TimeSpan reconnectDelay = default,
        int maxReconnectAttempts = DefaultMaxReconnectAttempts,
        TimeSpan pingInterval = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReconnectAttempts);

        this.baseUrl = baseUrl.TrimEnd('/');
        this.maxMessageSize = maxMessageSize;
        this.reconnectDelay = reconnectDelay == default ? DefaultReconnectDelay : reconnectDelay;
        this.maxReconnectAttempts = maxReconnectAttempts;
        this.pingInterval = pingInterval == default ? DefaultPingInterval : pingInterval;
    }

    /// <summary>
    /// Подключается к каналу рыночных данных и подписывается на указанные рынки/активы.
    /// </summary>
    /// <param name="markets">Идентификаторы рынков (condition ID) для подписки.</param>
    /// <param name="assetsIds">Идентификаторы активов для подписки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SubscribeMarketAsync(
        string[]? markets = null,
        string[]? assetsIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await marketLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Установка соединения, если ещё не подключены
            if (marketSocket is null || marketSocket.State != WebSocketState.Open)
            {
                marketCts?.Dispose();
                marketCts = new CancellationTokenSource();

                marketSocket?.Dispose();
                marketSocket = new ClientWebSocket();

                var uri = new Uri($"{baseUrl}/market");
                await marketSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                // Запуск цикла приёма сообщений
                marketReceiveLoop = Task.Run(
                    () => ReceiveLoopAsync(marketSocket, PolymarketChannel.Market, marketCts.Token),
                    CancellationToken.None);
            }

            // Отправка подписки
            var subscription = new PolymarketSubscription
            {
                Type = "subscribe",
                Channel = PolymarketChannel.Market,
                Markets = markets,
                AssetsIds = assetsIds
            };

            // Сохранение параметров для автоматического переподключения
            lastMarketSubscriptionMarkets = markets;
            lastMarketSubscriptionAssets = assetsIds;

            await SendAsync(marketSocket, subscription, cancellationToken).ConfigureAwait(false);

            // Запуск ping/pong keepalive
            marketPingLoop ??= Task.Run(
                () => PingLoopAsync(marketSocket, marketCts.Token),
                CancellationToken.None);
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <summary>
    /// Отписывается от указанных рынков/активов в канале рыночных данных.
    /// </summary>
    /// <param name="markets">Идентификаторы рынков для отписки.</param>
    /// <param name="assetsIds">Идентификаторы активов для отписки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask UnsubscribeMarketAsync(
        string[]? markets = null,
        string[]? assetsIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (marketSocket is null || marketSocket.State != WebSocketState.Open)
            throw new PolymarketException("Соединение с рыночным каналом не установлено.");

        var subscription = new PolymarketSubscription
        {
            Type = "unsubscribe",
            Channel = PolymarketChannel.Market,
            Markets = markets,
            AssetsIds = assetsIds
        };

        await SendAsync(marketSocket, subscription, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Подключается к каналу пользовательских данных с аутентификацией.
    /// </summary>
    /// <param name="credentials">Учётные данные для аутентификации API.</param>
    /// <param name="markets">Идентификаторы рынков для подписки (необязательно).</param>
    /// <param name="assetsIds">Идентификаторы активов для подписки (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SubscribeUserAsync(
        PolymarketAuth credentials,
        string[]? markets = null,
        string[]? assetsIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(credentials);

        auth = credentials;

        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Установка соединения, если ещё не подключены
            if (userSocket is null || userSocket.State != WebSocketState.Open)
            {
                userCts?.Dispose();
                userCts = new CancellationTokenSource();

                userSocket?.Dispose();
                userSocket = new ClientWebSocket();

                var uri = new Uri($"{baseUrl}/user");
                await userSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                // Запуск цикла приёма сообщений
                userReceiveLoop = Task.Run(
                    () => ReceiveLoopAsync(userSocket, PolymarketChannel.User, userCts.Token),
                    CancellationToken.None);
            }

            // Отправка подписки с аутентификацией
            var subscription = new PolymarketSubscription
            {
                Type = "subscribe",
                Channel = PolymarketChannel.User,
                Markets = markets,
                AssetsIds = assetsIds,
                Auth = credentials
            };

            // Сохранение параметров для автоматического переподключения
            lastUserSubscriptionMarkets = markets;
            lastUserSubscriptionAssets = assetsIds;

            await SendAsync(userSocket, subscription, cancellationToken).ConfigureAwait(false);

            // Запуск ping/pong keepalive
            userPingLoop ??= Task.Run(
                () => PingLoopAsync(userSocket, userCts.Token),
                CancellationToken.None);
        }
        finally
        {
            userLock.Release();
        }
    }

    /// <summary>
    /// Отписывается от указанных рынков/активов в канале пользователя.
    /// </summary>
    /// <param name="markets">Идентификаторы рынков для отписки.</param>
    /// <param name="assetsIds">Идентификаторы активов для отписки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask UnsubscribeUserAsync(
        string[]? markets = null,
        string[]? assetsIds = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (userSocket is null || userSocket.State != WebSocketState.Open)
            throw new PolymarketException("Соединение с каналом пользователя не установлено.");

        var subscription = new PolymarketSubscription
        {
            Type = "unsubscribe",
            Channel = PolymarketChannel.User,
            Markets = markets,
            AssetsIds = assetsIds,
            Auth = auth
        };

        await SendAsync(userSocket, subscription, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Закрывает соединение с рыночным каналом.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask DisconnectMarketAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await marketLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Отключение автоподключения для этого канала
            lastMarketSubscriptionMarkets = null;
            lastMarketSubscriptionAssets = null;

            await CloseSocketAsync(marketSocket, marketCts, marketReceiveLoop, marketPingLoop).ConfigureAwait(false);
            marketSocket = null;
            marketCts = null;
            marketReceiveLoop = null;
            marketPingLoop = null;
        }
        finally
        {
            marketLock.Release();
        }
    }

    /// <summary>
    /// Закрывает соединение с каналом пользователя.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask DisconnectUserAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Отключение автоподключения для этого канала
            lastUserSubscriptionMarkets = null;
            lastUserSubscriptionAssets = null;

            await CloseSocketAsync(userSocket, userCts, userReceiveLoop, userPingLoop).ConfigureAwait(false);
            userSocket = null;
            userCts = null;
            userReceiveLoop = null;
            userPingLoop = null;
        }
        finally
        {
            userLock.Release();
        }
    }

    /// <summary>
    /// Отправляет сообщение подписки через указанный WebSocket.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask SendAsync(
        ClientWebSocket socket,
        PolymarketSubscription subscription,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(subscription, PolymarketJsonContext.Default.PolymarketSubscription);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Основной цикл приёма и обработки входящих сообщений WebSocket.
    /// При разрыве соединения инициирует автоматическое переподключение.
    /// </summary>
    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        PolymarketChannel channel,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxMessageSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var (count, messageType) = await ReceiveFullMessageAsync(socket, buffer, cancellationToken).ConfigureAwait(false);

                if (messageType == WebSocketMessageType.Close)
                    break;

                if (messageType != WebSocketMessageType.Text || count == 0)
                    continue;

                await ProcessMessageAsync(buffer.AsMemory(0, count), channel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await OnDisconnectedAsync(channel).ConfigureAwait(false);

            // Попытка автоматического переподключения
            if (autoReconnectEnabled && !isDisposed && !cancellationToken.IsCancellationRequested)
                _ = Task.Run(() => ReconnectAsync(channel, cancellationToken), CancellationToken.None);
        }
    }

    /// <summary>
    /// Цикл отправки ping-кадров для поддержания WebSocket-соединения.
    /// </summary>
    private async Task PingLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await Task.Delay(pingInterval, cancellationToken).ConfigureAwait(false);

                if (socket.State != WebSocketState.Open)
                    break;

                try
                {
                    // Отправка пустого ping-кадра
                    await socket.SendAsync(
                        ReadOnlyMemory<byte>.Empty,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException) { break; }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Выполняет автоматическое переподключение с экспоненциальным backoff.
    /// Восстанавливает ранее активные подписки после успешного подключения.
    /// </summary>
    private async Task ReconnectAsync(PolymarketChannel channel, CancellationToken cancellationToken)
    {
        var currentDelay = reconnectDelay;
        var attempt = 0;

        while (!isDisposed && !cancellationToken.IsCancellationRequested)
        {
            attempt++;

            // Проверка лимита попыток (0 = безлимитно)
            if (maxReconnectAttempts > 0 && attempt > maxReconnectAttempts)
                return;

            try
            {
                await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);

                if (isDisposed)
                    return;

                switch (channel)
                {
                    case PolymarketChannel.Market:
                        await SubscribeMarketAsync(
                            lastMarketSubscriptionMarkets,
                            lastMarketSubscriptionAssets,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    case PolymarketChannel.User when auth is not null:
                        await SubscribeUserAsync(
                            auth,
                            lastUserSubscriptionMarkets,
                            lastUserSubscriptionAssets,
                            cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        return; // Нет сохранённых данных для переподключения
                }

                // Успешное переподключение
                if (Reconnected is { } handler)
                    await handler(this, new PolymarketReconnectedEventArgs(channel, attempt)).ConfigureAwait(false);

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                // Экспоненциальный backoff с ограничением сверху
                currentDelay = TimeSpan.FromTicks(Math.Min(
                    currentDelay.Ticks * 2,
                    MaxReconnectDelay.Ticks));
            }
            catch (PolymarketException)
            {
                currentDelay = TimeSpan.FromTicks(Math.Min(
                    currentDelay.Ticks * 2,
                    MaxReconnectDelay.Ticks));
            }
        }
    }

    /// <summary>
    /// Принимает полное WebSocket-сообщение, собирая все фрагменты.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<(int Count, WebSocketMessageType MessageType)> ReceiveFullMessageAsync(
        ClientWebSocket socket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalBytes = 0;

        ValueWebSocketReceiveResult result;
        do
        {
            if (totalBytes >= buffer.Length)
                throw new PolymarketException("Размер сообщения превышает допустимый предел.");

            result = await socket.ReceiveAsync(
                buffer.AsMemory(totalBytes),
                cancellationToken).ConfigureAwait(false);

            totalBytes += result.Count;
        }
        while (!result.EndOfMessage);

        return (totalBytes, result.MessageType);
    }

    /// <summary>
    /// Обрабатывает входящее сообщение, определяя его тип и вызывая соответствующее событие.
    /// </summary>
    internal async ValueTask ProcessMessageAsync(ReadOnlyMemory<byte> data, PolymarketChannel channel)
    {
        PolymarketMessage? message;

        try
        {
            message = JsonSerializer.Deserialize(data.Span, PolymarketJsonContext.Default.PolymarketMessage);
        }
        catch (JsonException ex)
        {
            if (ErrorOccurred is { } errorHandler)
                await errorHandler(this, new PolymarketErrorEventArgs(ex, channel)).ConfigureAwait(false);
            return;
        }

        if (message is null)
            return;

        switch (message.EventType)
        {
            case PolymarketEventType.Book when BookSnapshotReceived is { } handler:
                await handler(this, new PolymarketBookEventArgs(new PolymarketBookSnapshot
                {
                    EventType = message.EventType,
                    AssetId = message.AssetId,
                    Market = message.Market,
                    Timestamp = message.Timestamp,
                    Hash = message.Hash,
                    Buys = message.Buys,
                    Sells = message.Sells
                })).ConfigureAwait(false);
                break;

            case PolymarketEventType.PriceChange when PriceChanged is { } handler:
                await handler(this, new PolymarketPriceChangeEventArgs(new PolymarketPriceChange
                {
                    EventType = message.EventType,
                    AssetId = message.AssetId,
                    Market = message.Market,
                    Changes = message.Changes
                })).ConfigureAwait(false);
                break;

            case PolymarketEventType.LastTradePrice when LastTradePriceReceived is { } handler:
                await handler(this, new PolymarketLastTradePriceEventArgs(new PolymarketLastTradePrice
                {
                    EventType = message.EventType,
                    AssetId = message.AssetId,
                    Market = message.Market,
                    Price = message.Price
                })).ConfigureAwait(false);
                break;

            case PolymarketEventType.TickSizeChange when TickSizeChanged is { } handler:
                await handler(this, new PolymarketTickSizeChangeEventArgs(new PolymarketTickSizeChange
                {
                    EventType = message.EventType,
                    AssetId = message.AssetId,
                    Market = message.Market,
                    OldTickSize = message.OldTickSize,
                    NewTickSize = message.NewTickSize
                })).ConfigureAwait(false);
                break;

            case PolymarketEventType.Order when message.Order is not null && OrderUpdated is { } handler:
                await handler(this, new PolymarketOrderEventArgs(message.Order)).ConfigureAwait(false);
                break;

            case PolymarketEventType.Trade when message.Trade is not null && TradeReceived is { } handler:
                await handler(this, new PolymarketTradeEventArgs(message.Trade)).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Вызывается при разрыве соединения с каналом.
    /// </summary>
    private async ValueTask OnDisconnectedAsync(PolymarketChannel channel)
    {
        var handler = channel switch
        {
            PolymarketChannel.Market => MarketDisconnected,
            PolymarketChannel.User => UserDisconnected,
            _ => null
        };

        if (handler is not null)
            await handler(this, new PolymarketDisconnectedEventArgs(channel)).ConfigureAwait(false);
    }

    /// <summary>
    /// Безопасно закрывает WebSocket-соединение и ожидает завершения циклов приёма и ping.
    /// </summary>
    private static async ValueTask CloseSocketAsync(
        ClientWebSocket? socket,
        CancellationTokenSource? cts,
        Task? receiveLoop,
        Task? pingLoop)
    {
        if (cts is not null)
            await cts.CancelAsync().ConfigureAwait(false);

        if (socket is not null && socket.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Клиент завершает работу.",
                    closeCts.Token).ConfigureAwait(false);
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
        }

        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }

        if (pingLoop is not null)
        {
            try
            {
                await pingLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }

        socket?.Dispose();
        cts?.Dispose();
    }

    /// <summary>
    /// Асинхронно освобождает все ресурсы клиента.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        // Отключение автопереподключения
        autoReconnectEnabled = false;

        await CloseSocketAsync(marketSocket, marketCts, marketReceiveLoop, marketPingLoop).ConfigureAwait(false);
        await CloseSocketAsync(userSocket, userCts, userReceiveLoop, userPingLoop).ConfigureAwait(false);

        marketLock.Dispose();
        userLock.Dispose();
    }

    /// <summary>
    /// Синхронно освобождает все ресурсы клиента.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        // Отключение автопереподключения
        autoReconnectEnabled = false;

        marketCts?.Cancel();
        userCts?.Cancel();

        marketSocket?.Dispose();
        userSocket?.Dispose();
        marketCts?.Dispose();
        userCts?.Dispose();
        marketLock.Dispose();
        userLock.Dispose();
    }
}
