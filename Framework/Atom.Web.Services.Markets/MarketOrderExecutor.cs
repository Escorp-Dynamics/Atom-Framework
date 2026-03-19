using System.Collections.Concurrent;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Авто-исполнитель ордеров: оценивает стратегии → выставляет ордера.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация авто-исполнителя ордеров.
/// </summary>
public sealed class MarketOrderExecutor : IMarketOrderExecutor
{
    private readonly IMarketPriceStream priceStream;
    private readonly IMarketRestClient restClient;
    private readonly ConcurrentDictionary<string, StrategyBinding> bindings = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastOrderTime = new();
    private Timer? evaluationTimer;
    private int evaluationInProgress;
    private bool isDisposed;

    /// <summary>Событие: сигнал сгенерирован.</summary>
    public event Action<IMarketTradeSignal>? OnSignalGenerated;

    /// <summary>Событие: ордер исполнен.</summary>
    public event Action<string, string?>? OnOrderExecuted; // assetId, orderId

    /// <summary>
    /// Создаёт исполнитель ордеров.
    /// </summary>
    public MarketOrderExecutor(IMarketPriceStream priceStream, IMarketRestClient restClient)
    {
        this.priceStream = priceStream;
        this.restClient = restClient;
    }

    /// <inheritdoc />
    public bool DryRun { get; set; }

    /// <inheritdoc />
    public double MinConfidence { get; set; } = 0.5;

    /// <inheritdoc />
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public TimeSpan OrderCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public void AddStrategy(IMarketStrategy strategy, string[] assetIds)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        bindings[strategy.Name] = new StrategyBinding(strategy, assetIds);
    }

    /// <inheritdoc />
    public void RemoveStrategy(string strategyName) => bindings.TryRemove(strategyName, out _);

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        evaluationTimer ??= new Timer(
            _ => _ = EvaluateOnceAsync(),
            null, TimeSpan.Zero, EvaluationInterval);
    }

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        if (evaluationTimer is not null)
        {
            var t = evaluationTimer;
            evaluationTimer = null;
            return new ValueTask(t.DisposeAsync().AsTask());
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask EvaluateOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (Interlocked.CompareExchange(ref evaluationInProgress, 1, 0) != 0)
            return;

        try
        {
            foreach (var (_, binding) in bindings)
            {
                foreach (var assetId in binding.AssetIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Обновляем стратегию с текущим снимком
                    var snapshot = priceStream.GetPrice(assetId);
                    if (snapshot is not null)
                        binding.Strategy.OnPriceUpdated(snapshot);

                    // Оцениваем
                    var signal = binding.Strategy.Evaluate(priceStream, assetId);
                    if (signal.Action == TradeAction.Hold) continue;
                    if (signal.Confidence < MinConfidence) continue;

                    OnSignalGenerated?.Invoke(signal);

                    // Проверяем cooldown
                    if (lastOrderTime.TryGetValue(assetId, out var lastTime) &&
                        DateTimeOffset.UtcNow - lastTime < OrderCooldown)
                        continue;

                    // Исполняем
                    if (!DryRun)
                    {
                        var side = signal.Action == TradeAction.Buy ? TradeSide.Buy : TradeSide.Sell;
                        double? price = signal.Price is not null && double.TryParse(signal.Price, out var p) ? p : null;
                        var orderId = await restClient.CreateOrderAsync(assetId, side, signal.Quantity, price, cancellationToken)
                            .ConfigureAwait(false);
                        OnOrderExecuted?.Invoke(assetId, orderId);
                    }
                    else
                    {
                        OnOrderExecuted?.Invoke(assetId, $"DRY-{signal.Action}-{signal.Quantity:G}");
                    }

                    lastOrderTime[assetId] = DateTimeOffset.UtcNow;
                }
            }
        }
        finally
        {
            Volatile.Write(ref evaluationInProgress, 0);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;
        await StopAsync().ConfigureAwait(false);
        bindings.Clear();
        lastOrderTime.Clear();
    }

    private sealed record StrategyBinding(IMarketStrategy Strategy, string[] AssetIds);
}
