using System.Threading.Channels;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Streaming Pipeline: WS → PriceStream → Стратегии → Ордера.
// Единый конвейер обработки рыночных данных в реальном времени.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Конвейер обработки рыночных данных: приём цен → кеш → оценка стратегий → исполнение.
/// </summary>
public interface IMarketStreamingPipeline : IAsyncDisposable
{
    /// <summary>Платформа, к которой привязан конвейер.</summary>
    string PlatformName { get; }

    /// <summary>Запущен ли конвейер.</summary>
    bool IsRunning { get; }

    /// <summary>Количество обработанных обновлений цен.</summary>
    long ProcessedUpdates { get; }

    /// <summary>Количество сгенерированных сигналов.</summary>
    long GeneratedSignals { get; }

    /// <summary>Количество исполненных ордеров.</summary>
    long ExecutedOrders { get; }

    /// <summary>
    /// Запускает конвейер: подключение к WS, подписка на активы, фоновая обработка.
    /// </summary>
    /// <param name="assetIds">Активы для подписки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask StartAsync(string[] assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает конвейер: отписка, отключение WS, остановка фоновых задач.
    /// </summary>
    ValueTask StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет стратегию в конвейер.
    /// </summary>
    void AddStrategy(IMarketStrategy strategy, string[] assetIds);

    /// <summary>
    /// Удаляет стратегию из конвейера.
    /// </summary>
    void RemoveStrategy(string strategyName);
}

/// <summary>
/// Обновление цены, передаваемое через канал конвейера.
/// </summary>
public readonly record struct PriceUpdate(string AssetId, double Bid, double Ask, double LastPrice, long Ticks);

/// <summary>
/// Сигнал, сгенерированный стратегией в конвейере.
/// </summary>
public readonly record struct PipelineSignal(string StrategyName, IMarketTradeSignal Signal);

/// <summary>
/// Конфигурация streaming pipeline.
/// </summary>
public sealed class StreamingPipelineConfig
{
    /// <summary>Интервал между оценками стратегий.</summary>
    public TimeSpan EvaluationInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Cooldown между ордерами по одному активу.</summary>
    public TimeSpan OrderCooldown { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Минимальная уверенность для исполнения сигнала.</summary>
    public double MinConfidence { get; init; } = 0.7;

    /// <summary>DryRun — не отправлять реальные ордера.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Ёмкость канала обновлений цен (bounded channel).</summary>
    public int PriceChannelCapacity { get; init; } = 10_000;

    /// <summary>Ёмкость канала сигналов (bounded channel).</summary>
    public int SignalChannelCapacity { get; init; } = 1_000;

    /// <summary>Автоматический reconnect при обрыве WS.</summary>
    public bool AutoReconnect { get; init; } = true;

    /// <summary>Задержка перед reconnect.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Максимум попыток reconnect.</summary>
    public int MaxReconnectAttempts { get; init; } = 10;
}

/// <summary>
/// Реализация streaming pipeline: WS → Channel → PriceStream → Strategies → Orders.
/// </summary>
/// <remarks>
/// Архитектура: три фоновых этапа связанных через <see cref="Channel{T}"/>:
/// <list type="number">
///   <item>WS Reader → пишет <see cref="PriceUpdate"/> в priceChannel</item>
///   <item>Strategy Evaluator → читает из priceChannel, обновляет кеш, оценивает стратегии → пишет в signalChannel</item>
///   <item>Order Executor → читает из signalChannel, исполняет ордера через REST</item>
/// </list>
/// </remarks>
public sealed class MarketStreamingPipeline : IMarketStreamingPipeline
{
    private readonly IMarketClient wsClient;
    private readonly IMarketPriceStream priceStream;
    private readonly IMarketRestClient? restClient;
    private readonly StreamingPipelineConfig config;

    private readonly List<(IMarketStrategy Strategy, string[] AssetIds)> strategies = [];
    private readonly Lock strategiesLock = new();

    private Channel<PriceUpdate>? priceChannel;
    private Channel<PipelineSignal>? signalChannel;
    private CancellationTokenSource? cts;
    private Task? evaluatorTask;
    private Task? executorTask;

    private long processedUpdates;
    private long generatedSignals;
    private long executedOrders;
    private volatile bool isRunning;
    private bool isDisposed;

    /// <summary>
    /// Создаёт streaming pipeline.
    /// </summary>
    /// <param name="wsClient">WebSocket-клиент для приёма данных.</param>
    /// <param name="priceStream">Кеш цен для обновления.</param>
    /// <param name="restClient">REST-клиент для исполнения ордеров (null для read-only режима).</param>
    /// <param name="config">Конфигурация конвейера.</param>
    public MarketStreamingPipeline(
        IMarketClient wsClient,
        IMarketPriceStream priceStream,
        IMarketRestClient? restClient = null,
        StreamingPipelineConfig? config = null)
    {
        this.wsClient = wsClient;
        this.priceStream = priceStream;
        this.restClient = restClient;
        this.config = config ?? new StreamingPipelineConfig();
    }

    /// <inheritdoc />
    public string PlatformName => wsClient.PlatformName;

    /// <inheritdoc />
    public bool IsRunning => isRunning;

    /// <inheritdoc />
    public long ProcessedUpdates => Interlocked.Read(ref processedUpdates);

    /// <inheritdoc />
    public long GeneratedSignals => Interlocked.Read(ref generatedSignals);

    /// <inheritdoc />
    public long ExecutedOrders => Interlocked.Read(ref executedOrders);

    /// <summary>
    /// Событие: получен сигнал от стратегии. Позволяет внешнему коду логировать/фильтровать сигналы.
    /// </summary>
    public event Action<PipelineSignal>? OnSignalGenerated;

    /// <summary>
    /// Событие: ордер исполнен (или симулирован в DryRun).
    /// </summary>
    public event Action<PipelineSignal, string?>? OnOrderExecuted;

    /// <inheritdoc />
    public async ValueTask StartAsync(string[] assetIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (isRunning) return;

        // Создаём bounded channels
        priceChannel = Channel.CreateBounded<PriceUpdate>(new BoundedChannelOptions(config.PriceChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

        signalChannel = Channel.CreateBounded<PipelineSignal>(new BoundedChannelOptions(config.SignalChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

        cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

        // Подключение и подписка
        await wsClient.SubscribeAsync(assetIds, token).ConfigureAwait(false);

        // Запуск фоновых этапов
        evaluatorTask = Task.Run(() => EvaluatorLoopAsync(token), token);
        executorTask = Task.Run(() => ExecutorLoopAsync(token), token);

        isRunning = true;
    }

    /// <summary>
    /// Публикует обновление цены в канал (вызывается из WS reader).
    /// </summary>
    public bool PublishPriceUpdate(PriceUpdate update)
    {
        if (priceChannel is null || !isRunning) return false;
        return priceChannel.Writer.TryWrite(update);
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!isRunning) return;
        isRunning = false;

        // Закрываем каналы
        priceChannel?.Writer.TryComplete();
        signalChannel?.Writer.TryComplete();

        // Отменяем фоновые задачи
        if (cts is not null)
            await cts.CancelAsync().ConfigureAwait(false);

        // Ждём завершения
        if (evaluatorTask is not null)
            await evaluatorTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (executorTask is not null)
            await executorTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Отключаемся от WS
        await wsClient.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        cts?.Dispose();
        cts = null;
    }

    /// <inheritdoc />
    public void AddStrategy(IMarketStrategy strategy, string[] assetIds)
    {
        lock (strategiesLock)
            strategies.Add((strategy, assetIds));
    }

    /// <inheritdoc />
    public void RemoveStrategy(string strategyName)
    {
        lock (strategiesLock)
            strategies.RemoveAll(s => s.Strategy.Name == strategyName);
    }

    /// <summary>
    /// Этап 2: Читает цены из канала → обновляет кеш → оценивает стратегии → пишет сигналы.
    /// </summary>
    private async Task EvaluatorLoopAsync(CancellationToken cancellationToken)
    {
        if (priceChannel is null || signalChannel is null) return;

        var reader = priceChannel.Reader;
        var signalWriter = signalChannel.Writer;

        await foreach (var update in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref processedUpdates);

            // Обновляем кеш цен
            var snapshot = new MarketPriceUpdate
            {
                AssetId = update.AssetId,
                BestBid = update.Bid,
                BestAsk = update.Ask,
                LastTradePrice = update.LastPrice,
                LastUpdateTicks = update.Ticks
            };

            // Уведомляем стратегии о новой цене
            (IMarketStrategy Strategy, string[] AssetIds)[] currentStrategies;
            lock (strategiesLock)
                currentStrategies = [.. strategies];

            foreach (var (strategy, assetIds) in currentStrategies)
            {
                if (!Array.Exists(assetIds, id => id == update.AssetId))
                    continue;

                strategy.OnPriceUpdated(snapshot);

                var signal = strategy.Evaluate(priceStream, update.AssetId);

                if (signal.Action != TradeAction.Hold)
                {
                    Interlocked.Increment(ref generatedSignals);
                    var pipelineSignal = new PipelineSignal(strategy.Name, signal);
                    OnSignalGenerated?.Invoke(pipelineSignal);
                    await signalWriter.WriteAsync(pipelineSignal, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Этап 3: Читает сигналы из канала → исполняет ордера через REST (или DryRun).
    /// </summary>
    private async Task ExecutorLoopAsync(CancellationToken cancellationToken)
    {
        if (signalChannel is null) return;

        var reader = signalChannel.Reader;
        var lastOrderTime = new Dictionary<string, long>();

        await foreach (var pipelineSignal in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var signal = pipelineSignal.Signal;

            // Проверка уверенности
            if (signal.Confidence < config.MinConfidence)
                continue;

            // Проверка cooldown
            var now = Environment.TickCount64;
            if (lastOrderTime.TryGetValue(signal.AssetId, out var lastTime)
                && now - lastTime < config.OrderCooldown.TotalMilliseconds)
                continue;

            string? orderId = null;

            if (!config.DryRun && restClient is not null)
            {
                var side = signal.Action == TradeAction.Buy ? TradeSide.Buy : TradeSide.Sell;
                double? price = signal.Price is not null
                    && double.TryParse(signal.Price, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var p)
                    ? p : null;

                orderId = await restClient.CreateOrderAsync(
                    signal.AssetId, side, signal.Quantity, price, cancellationToken).ConfigureAwait(false);
            }

            lastOrderTime[signal.AssetId] = now;
            Interlocked.Increment(ref executedOrders);
            OnOrderExecuted?.Invoke(pipelineSignal, orderId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await StopAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Внутренний снимок цены для конвейера (не привязан к конкретной бирже).
/// </summary>
internal sealed class MarketPriceUpdate : IMarketPriceSnapshot
{
    public required string AssetId { get; init; }
    public double? BestBid { get; set; }
    public double? BestAsk { get; set; }
    public double? Midpoint => (BestBid + BestAsk) / 2.0;
    public double? LastTradePrice { get; set; }
    public long LastUpdateTicks { get; set; }
}
