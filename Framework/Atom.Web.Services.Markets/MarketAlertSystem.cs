using System.Collections.Concurrent;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Система алертов: мониторинг ценовых и P&L-условий.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация системы алертов.
/// </summary>
public sealed class MarketAlertSystem : IMarketAlertSystem
{
    private readonly ConcurrentDictionary<string, IMarketAlertDefinition> alerts = new();
    private readonly IMarketPriceStream priceStream;
    private readonly IMarketPortfolioTracker? portfolioTracker;
    private bool isDisposed;

    /// <summary>Событие: алерт сработал.</summary>
    public event Action<IMarketAlertDefinition, double>? OnAlertTriggered;

    /// <summary>
    /// Создаёт систему алертов.
    /// </summary>
    /// <param name="priceStream">Источник цен для PriceThreshold.</param>
    /// <param name="portfolioTracker">Трекер портфеля для PnL алертов (опционально).</param>
    public MarketAlertSystem(IMarketPriceStream priceStream, IMarketPortfolioTracker? portfolioTracker = null)
    {
        this.priceStream = priceStream;
        this.portfolioTracker = portfolioTracker;
    }

    /// <inheritdoc />
    public int ActiveCount => alerts.Count(kv => kv.Value.IsEnabled && !kv.Value.HasTriggered);

    /// <inheritdoc />
    public void AddAlert(IMarketAlertDefinition alert)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        alerts[alert.Id] = alert;
    }

    /// <inheritdoc />
    public void RemoveAlert(string alertId) => alerts.TryRemove(alertId, out _);

    /// <inheritdoc />
    public IMarketAlertDefinition? GetAlert(string alertId) =>
        alerts.TryGetValue(alertId, out var alert) ? alert : null;

    /// <inheritdoc />
    public void ClearAlerts() => alerts.Clear();

    /// <inheritdoc />
    public void DisconnectAll()
    {
        foreach (var alert in alerts.Values)
            alert.IsEnabled = false;
    }

    /// <summary>
    /// Проверяет все активные алерты.
    /// </summary>
    public void CheckAlerts()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        foreach (var (_, alert) in alerts)
        {
            if (!alert.IsEnabled || alert.HasTriggered) continue;

            var currentValue = GetCurrentValue(alert);
            if (currentValue is null) continue;

            var triggered = alert.Direction == AlertDirection.Above
                ? currentValue.Value >= alert.Threshold
                : currentValue.Value <= alert.Threshold;

            if (triggered)
            {
                alert.HasTriggered = true;
                OnAlertTriggered?.Invoke(alert, currentValue.Value);

                if (alert.OneShot)
                    alert.IsEnabled = false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        alerts.Clear();
    }

    private double? GetCurrentValue(IMarketAlertDefinition alert) => alert.Condition switch
    {
        AlertCondition.PriceThreshold when alert.AssetId is not null =>
            priceStream.GetPrice(alert.AssetId)?.LastTradePrice,

        AlertCondition.PnLThreshold when alert.AssetId is not null && portfolioTracker is not null =>
            portfolioTracker.GetPosition(alert.AssetId)?.UnrealizedPnL,

        AlertCondition.PortfolioPnLThreshold when portfolioTracker is not null =>
            portfolioTracker.GetSummary().NetPnL,

        _ => null
    };
}

/// <summary>Конкретное определение алерта.</summary>
public sealed class AlertDefinition : IMarketAlertDefinition
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public required AlertCondition Condition { get; init; }

    /// <inheritdoc />
    public required AlertDirection Direction { get; init; }

    /// <inheritdoc />
    public required double Threshold { get; init; }

    /// <inheritdoc />
    public string? AssetId { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <inheritdoc />
    public bool OneShot { get; init; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public bool HasTriggered { get; set; }
}
