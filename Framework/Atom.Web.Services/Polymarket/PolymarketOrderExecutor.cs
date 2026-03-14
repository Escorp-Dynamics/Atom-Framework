using System.Collections.Concurrent;

using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Аргументы события исполнения ордера.
/// </summary>
public sealed class PolymarketOrderExecutedEventArgs(
    PolymarketTradeSignal signal,
    PolymarketOrderResponse? response,
    bool success) : EventArgs
{
    /// <summary>Сигнал стратегии, вызвавший ордер.</summary>
    public PolymarketTradeSignal Signal { get; } = signal;

    /// <summary>Ответ API (null при ошибке до отправки).</summary>
    public PolymarketOrderResponse? Response { get; } = response;

    /// <summary>Успешность исполнения.</summary>
    public bool Success { get; } = success;

    /// <summary>Ошибка, если исполнение не удалось.</summary>
    public Exception? Error { get; init; }

    /// <summary>Время исполнения.</summary>
    public DateTimeOffset ExecutedAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Аргументы события генерации сигнала стратегией.
/// </summary>
public sealed class PolymarketSignalGeneratedEventArgs(
    PolymarketTradeSignal signal,
    string strategyName) : EventArgs
{
    /// <summary>Сгенерированный сигнал.</summary>
    public PolymarketTradeSignal Signal { get; } = signal;

    /// <summary>Имя стратегии.</summary>
    public string StrategyName { get; } = strategyName;
}

/// <summary>
/// Автоматический исполнитель ордеров — связывает стратегии с REST-клиентом.
/// Подписывается на PriceStream, получает сигналы от стратегий и размещает ордера.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Потокобезопасен.
/// </remarks>
public sealed class PolymarketOrderExecutor : IMarketOrderExecutor, IAsyncDisposable, IDisposable
{
    private readonly PolymarketRestClient restClient;
    private readonly PolymarketPriceStream priceStream;
    private readonly ConcurrentDictionary<string, IPolymarketStrategy> strategies = new();
    private readonly ConcurrentDictionary<string, string[]> strategyAssets = new();
    private readonly ConcurrentDictionary<string, long> cooldowns = new();

    private CancellationTokenSource? evaluationCts;
    private Task? evaluationTask;
    private bool isDisposed;

    /// <summary>
    /// Минимальный интервал между ордерами по одному активу (по умолчанию 60 секунд).
    /// </summary>
    public TimeSpan OrderCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Минимальная уверенность сигнала для исполнения (по умолчанию 0.3).
    /// </summary>
    public double MinConfidence { get; set; } = 0.3;

    /// <summary>
    /// Режим "сухой прогонки" — сигналы генерируются, но ордера не отправляются (по умолчанию true).
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Интервал оценки стратегий (по умолчанию 30 секунд).
    /// </summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Данные для создания ордеров (владелец, подписчик).
    /// </summary>
    public string? OwnerAddress { get; set; }

    /// <summary>
    /// Событие при генерации сигнала (до исполнения).
    /// </summary>
    public event AsyncEventHandler<PolymarketOrderExecutor, PolymarketSignalGeneratedEventArgs>? SignalGenerated;

    /// <summary>
    /// Событие при исполнении (или неудаче) ордера.
    /// </summary>
    public event AsyncEventHandler<PolymarketOrderExecutor, PolymarketOrderExecutedEventArgs>? OrderExecuted;

    /// <summary>
    /// Инициализирует исполнитель ордеров.
    /// </summary>
    /// <param name="restClient">REST-клиент для размещения ордеров.</param>
    /// <param name="priceStream">Поток цен для подписки стратегий.</param>
    public PolymarketOrderExecutor(PolymarketRestClient restClient, PolymarketPriceStream priceStream)
    {
        ArgumentNullException.ThrowIfNull(restClient);
        ArgumentNullException.ThrowIfNull(priceStream);

        this.restClient = restClient;
        this.priceStream = priceStream;

        // Подписываемся на обновления цен для стратегий
        priceStream.PriceUpdated += OnPriceUpdatedAsync;
    }

    /// <summary>
    /// Количество подключённых стратегий.
    /// </summary>
    public int StrategyCount => strategies.Count;

    /// <summary>
    /// Работает ли цикл оценки.
    /// </summary>
    public bool IsRunning => evaluationTask is not null && !evaluationTask.IsCompleted;

    /// <summary>
    /// Добавляет стратегию с привязкой к активам.
    /// </summary>
    /// <param name="strategy">Стратегия.</param>
    /// <param name="assetIds">Активы, по которым стратегия генерирует сигналы.</param>
    public void AddStrategy(IPolymarketStrategy strategy, string[] assetIds)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(assetIds);

        strategies[strategy.Name] = strategy;
        strategyAssets[strategy.Name] = assetIds;
    }

    // IMarketOrderExecutor — явная реализация
    void IMarketOrderExecutor.AddStrategy(IMarketStrategy strategy, string[] assetIds) =>
        AddStrategy((IPolymarketStrategy)strategy, assetIds);

    /// <summary>
    /// Удаляет стратегию.
    /// </summary>
    public void RemoveStrategy(string strategyName)
    {
        strategies.TryRemove(strategyName, out _);
        strategyAssets.TryRemove(strategyName, out _);
    }

    /// <summary>
    /// Запускает автоматический цикл оценки стратегий.
    /// </summary>
    public void Start()
    {
        if (evaluationCts is not null) return;

        evaluationCts = new CancellationTokenSource();
        evaluationTask = EvaluationLoopAsync(evaluationCts.Token);
    }

    /// <summary>
    /// Останавливает автоматический цикл оценки.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (evaluationCts is null) return;

        await evaluationCts.CancelAsync().ConfigureAwait(false);

        if (evaluationTask is not null)
        {
            try { await evaluationTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        evaluationCts.Dispose();
        evaluationCts = null;
        evaluationTask = null;
    }

    /// <summary>
    /// Выполняет единичную оценку всех стратегий и исполняет сигналы.
    /// </summary>
    public async ValueTask EvaluateOnceAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (name, strategy) in strategies)
        {
            if (!strategyAssets.TryGetValue(name, out var assets)) continue;

            foreach (var assetId in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EvaluateAssetAsync(name, strategy, assetId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Оценивает один актив по стратегии и исполняет сигнал.
    /// </summary>
    private async ValueTask EvaluateAssetAsync(
        string strategyName, IPolymarketStrategy strategy, string assetId,
        CancellationToken cancellationToken)
    {
        var signal = strategy.Evaluate(priceStream, assetId);

        if (signal.Action == PolymarketTradeAction.Hold) return;
        if (signal.Confidence < MinConfidence) return;

        // Проверяем cooldown
        if (cooldowns.TryGetValue(assetId, out var lastExec))
        {
            var elapsed = Environment.TickCount64 - lastExec;
            if (elapsed < OrderCooldown.TotalMilliseconds) return;
        }

        // Генерируем событие сигнала
        if (SignalGenerated is not null)
            await SignalGenerated.Invoke(this, new PolymarketSignalGeneratedEventArgs(signal, strategyName)).ConfigureAwait(false);

        // Исполняем ордер
        await ExecuteSignalAsync(signal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Исполняет конкретный сигнал (отправляет ордер через REST или логирует для DryRun).
    /// </summary>
    private async ValueTask ExecuteSignalAsync(PolymarketTradeSignal signal, CancellationToken cancellationToken)
    {
        // Обновляем cooldown
        cooldowns[signal.AssetId] = Environment.TickCount64;

        if (DryRun)
        {
            await NotifyOrderExecutedAsync(signal, null, true).ConfigureAwait(false);
            return;
        }

        try
        {
            var request = CreateOrderRequest(signal);
            var response = await restClient.CreateOrderAsync(request, cancellationToken).ConfigureAwait(false);
            await NotifyOrderExecutedAsync(signal, response, response?.Success ?? false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (OrderExecuted is not null)
            {
                await OrderExecuted.Invoke(this, new PolymarketOrderExecutedEventArgs(
                    signal, null, false) { Error = ex }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Создаёт запрос на ордер.
    /// </summary>
    private PolymarketCreateOrderRequest CreateOrderRequest(PolymarketTradeSignal signal)
    {
        var order = new PolymarketSignedOrder
        {
            Salt = PolymarketApiSigner.GenerateNonce(),
            Maker = OwnerAddress ?? "",
            Signer = OwnerAddress ?? "",
            Taker = "0x0000000000000000000000000000000000000000",
            TokenId = signal.AssetId,
            MakerAmount = ((long)(signal.Quantity * 1_000_000)).ToString(),
            TakerAmount = ((long)(signal.Quantity * 1_000_000)).ToString(),
            Expiration = "0",
            Nonce = "0",
            FeeRateBps = "0"
        };

        return new PolymarketCreateOrderRequest
        {
            Order = order,
            Owner = OwnerAddress ?? "",
            OrderType = PolymarketOrderType.GoodTilCancelled
        };
    }

    /// <summary>
    /// Уведомляет об исполнении ордера.
    /// </summary>
    private async ValueTask NotifyOrderExecutedAsync(
        PolymarketTradeSignal signal, PolymarketOrderResponse? response, bool success)
    {
        if (OrderExecuted is not null)
            await OrderExecuted.Invoke(this, new PolymarketOrderExecutedEventArgs(signal, response, success)).ConfigureAwait(false);
    }

    /// <summary>
    /// Фоновый цикл оценки стратегий.
    /// </summary>
    private async Task EvaluationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(EvaluationInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Тихо продолжаем — ошибки доставляются через OrderExecuted событие
            }
        }
    }

    /// <summary>
    /// Обработчик обновления цен — передаёт во все стратегии.
    /// </summary>
    private ValueTask OnPriceUpdatedAsync(PolymarketPriceStream sender, PolymarketPriceUpdatedEventArgs e)
    {
        foreach (var strategy in strategies.Values)
            strategy.OnPriceUpdated(e.Snapshot);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await StopAsync().ConfigureAwait(false);
        priceStream.PriceUpdated -= OnPriceUpdatedAsync;

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        evaluationCts?.Cancel();
        evaluationCts?.Dispose();
        priceStream.PriceUpdated -= OnPriceUpdatedAsync;

        GC.SuppressFinalize(this);
    }
}
