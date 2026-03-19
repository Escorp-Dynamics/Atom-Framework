using System.Collections.Concurrent;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Риск-менеджер: Stop-Loss, Take-Profit, Trailing Stop, позиционные
// лимиты, дневной лимит убытков.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация риск-менеджера.
/// </summary>
/// <remarks>
/// Хранит правила (<see cref="IMarketRiskRule"/>) для каждого актива.
/// <see cref="CheckAllRulesAsync"/> проверяет цены и срабатывание Stop-Loss/Take-Profit/Trailing Stop.
/// </remarks>
public sealed class MarketRiskManager : IMarketRiskManager
{
    private readonly ConcurrentDictionary<string, IMarketRiskRule> rules = new();
    private readonly IMarketPriceStream priceStream;
    private readonly IMarketRestClient? restClient;
    private readonly ConcurrentDictionary<string, double> highWaterMarks = new();
    private readonly Lock dailyLossLock = new();
    private double dailyLoss;
    private bool isDisposed;

    /// <summary>
    /// Создаёт риск-менеджер.
    /// </summary>
    /// <param name="priceStream">Источник цен для проверки правил.</param>
    /// <param name="restClient">REST-клиент для авто-исполнения (null = только уведомления).</param>
    /// <param name="limits">Лимиты портфеля.</param>
    public MarketRiskManager(IMarketPriceStream priceStream, IMarketRestClient? restClient = null, IMarketPortfolioLimits? limits = null)
    {
        this.priceStream = priceStream;
        this.restClient = restClient;
        Limits = limits ?? new PortfolioLimits();
    }

    /// <inheritdoc />
    public IMarketPortfolioLimits Limits { get; }

    /// <inheritdoc />
    public bool AutoExecute { get; set; }

    /// <inheritdoc />
    public double DailyLoss
    {
        get { lock (dailyLossLock) return dailyLoss; }
    }

    /// <summary>Событие: правило сработало.</summary>
    public event Action<IMarketRiskRule, string>? OnRuleTriggered;

    /// <inheritdoc />
    public void AddRule(IMarketRiskRule rule)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        rules[rule.AssetId] = rule;
    }

    /// <inheritdoc />
    public void RemoveRule(string assetId) => rules.TryRemove(assetId, out _);

    /// <inheritdoc />
    public IMarketRiskRule? GetRule(string assetId) =>
        rules.TryGetValue(assetId, out var rule) ? rule : null;

    /// <inheritdoc />
    public void ClearRules()
    {
        rules.Clear();
        highWaterMarks.Clear();
    }

    /// <inheritdoc />
    public bool CanOpenPosition(string assetId, double quantity)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var currentDailyLoss = DailyLoss;

        // Проверка дневного лимита убытков
        if (Limits.MaxDailyLoss > 0 && currentDailyLoss >= Limits.MaxDailyLoss)
            return false;

        // Проверка размера позиции
        if (Limits.MaxPositionSize > 0 && quantity > Limits.MaxPositionSize)
            return false;

        // Проверка максимума открытых позиций
        if (Limits.MaxOpenPositions > 0 && rules.Count >= Limits.MaxOpenPositions)
            return false;

        return true;
    }

    /// <inheritdoc />
    public async ValueTask CheckAllRulesAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        foreach (var (assetId, rule) in rules)
        {
            if (rule.IsTriggered) continue;

            var snapshot = priceStream.GetPrice(assetId);
            if (snapshot?.LastTradePrice is not { } price || price <= 0) continue;

            // Trailing Stop: обновляем high water mark
            if (rule.TrailingStopPercent is { } trailingPct && trailingPct > 0)
            {
                var hwm = highWaterMarks.AddOrUpdate(assetId, price, (_, prev) => Math.Max(prev, price));
                var trailingStop = hwm * (1 - trailingPct / 100);

                if (price <= trailingStop)
                {
                    await TriggerRuleAsync(rule, $"Trailing Stop: цена {price:F2} ≤ {trailingStop:F2} (HWM {hwm:F2}, trail {trailingPct}%)").ConfigureAwait(false);
                    continue;
                }
            }

            // Stop-Loss
            if (rule.StopLossPrice is { } sl && price <= sl)
            {
                await TriggerRuleAsync(rule, $"Stop-Loss: цена {price:F2} ≤ SL {sl:F2}").ConfigureAwait(false);
                continue;
            }

            // Take-Profit
            if (rule.TakeProfitPrice is { } tp && price >= tp)
            {
                await TriggerRuleAsync(rule, $"Take-Profit: цена {price:F2} ≥ TP {tp:F2}").ConfigureAwait(false);
                continue;
            }
        }
    }

    /// <inheritdoc />
    public void ResetDailyLoss()
    {
        lock (dailyLossLock)
            dailyLoss = 0;
    }

    /// <summary>Добавляет убыток к дневному счётчику.</summary>
    public void RecordLoss(double amount)
    {
        if (amount > 0)
        {
            lock (dailyLossLock)
                dailyLoss += amount;
        }
    }

    private async ValueTask TriggerRuleAsync(IMarketRiskRule rule, string reason)
    {
        rule.IsTriggered = true;
        OnRuleTriggered?.Invoke(rule, reason);

        if (AutoExecute && restClient is not null)
        {
            var quantity = Math.Abs(rule.PositionQuantity);
            if (quantity <= 0)
                throw new MarketException($"Невозможно авто-закрыть риск-правило для '{rule.AssetId}' без PositionQuantity > 0.");

            await restClient.CreateOrderAsync(
                rule.AssetId, TradeSide.Sell, quantity, null).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        rules.Clear();
        highWaterMarks.Clear();
    }
}

/// <summary>Правило риск-менеджмента.</summary>
public sealed class RiskRule : IMarketRiskRule
{
    /// <inheritdoc />
    public required string AssetId { get; init; }

    /// <inheritdoc />
    public double PositionQuantity { get; set; }

    /// <inheritdoc />
    public double? StopLossPrice { get; set; }

    /// <inheritdoc />
    public double? TakeProfitPrice { get; set; }

    /// <inheritdoc />
    public double? TrailingStopPercent { get; set; }

    /// <inheritdoc />
    public double? MaxLossPerPosition { get; set; }

    /// <inheritdoc />
    public bool IsTriggered { get; set; }
}

/// <summary>Лимиты портфеля.</summary>
public sealed class PortfolioLimits : IMarketPortfolioLimits
{
    /// <inheritdoc />
    public double MaxPositionSize { get; set; }

    /// <inheritdoc />
    public int MaxOpenPositions { get; set; }

    /// <inheritdoc />
    public double MaxPortfolioLoss { get; set; }

    /// <inheritdoc />
    public double MaxPositionPercent { get; set; }

    /// <inheritdoc />
    public double MaxDailyLoss { get; set; }
}
