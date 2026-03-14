using System.Collections.Concurrent;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Тип ордера риск-менеджмента.
/// </summary>
public enum PolymarketRiskOrderType : byte
{
    /// <summary>Stop-Loss — закрытие при убытке.</summary>
    StopLoss,

    /// <summary>Take-Profit — фиксация прибыли.</summary>
    TakeProfit,

    /// <summary>Trailing Stop — плавающий стоп.</summary>
    TrailingStop
}

/// <summary>
/// Аргументы события срабатывания стопа.
/// </summary>
public sealed class PolymarketRiskTriggeredEventArgs(
    string assetId,
    PolymarketRiskOrderType orderType,
    double triggerPrice,
    double currentPrice) : EventArgs
{
    /// <summary>Актив.</summary>
    public string AssetId { get; } = assetId;

    /// <summary>Тип стопа.</summary>
    public PolymarketRiskOrderType OrderType { get; } = orderType;

    /// <summary>Цена срабатывания.</summary>
    public double TriggerPrice { get; } = triggerPrice;

    /// <summary>Текущая цена на момент срабатывания.</summary>
    public double CurrentPrice { get; } = currentPrice;

    /// <summary>Время срабатывания.</summary>
    public DateTimeOffset TriggeredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Правило риск-менеджмента для одного актива.
/// </summary>
public sealed class PolymarketRiskRule
    : IMarketRiskRule
{
    /// <summary>Идентификатор актива.</summary>
    public required string AssetId { get; init; }

    /// <summary>Stop-loss цена (закрытие если цена ≤ этого значения).</summary>
    public double? StopLossPrice { get; set; }

    /// <summary>Take-profit цена (закрытие если цена ≥ этого значения).</summary>
    public double? TakeProfitPrice { get; set; }

    /// <summary>Trailing stop в процентах от максимума (например, 0.10 = 10%).</summary>
    public double? TrailingStopPercent { get; set; }

    /// <summary>Максимальный убыток на позицию в USDC (абсолютное значение).</summary>
    public double? MaxLossPerPosition { get; set; }

    /// <summary>Отслеживаемый максимум для trailing stop.</summary>
    internal double HighWaterMark { get; set; }

    /// <summary>Сработал ли стоп.</summary>
    public bool IsTriggered { get; set; }
}

/// <summary>
/// Глобальные лимиты портфеля.
/// </summary>
public sealed class PolymarketPortfolioLimits
    : IMarketPortfolioLimits
{
    /// <summary>Максимальный размер позиции в USDC (на один актив).</summary>
    public double MaxPositionSize { get; set; } = double.MaxValue;

    /// <summary>Максимальное количество одновременно открытых позиций.</summary>
    public int MaxOpenPositions { get; set; } = int.MaxValue;

    /// <summary>Максимальный суммарный убыток портфеля в USDC (при превышении — стоп всех позиций).</summary>
    public double MaxPortfolioLoss { get; set; } = double.MaxValue;

    /// <summary>Максимальная доля портфеля на один актив (0.0–1.0, например 0.25 = 25%).</summary>
    public double MaxPositionPercent { get; set; } = 1.0;

    /// <summary>Максимальный дневной убыток (сбрасывается вручную).</summary>
    public double MaxDailyLoss { get; set; } = double.MaxValue;
}

/// <summary>
/// Менеджер рисков Polymarket — Stop-Loss, Take-Profit, Trailing Stop, позиционные лимиты.
/// </summary>
/// <remarks>
/// Подключается к <see cref="PolymarketPriceStream"/> для мониторинга цен
/// и к <see cref="PolymarketPortfolioTracker"/> для расчёта лимитов.
/// Совместим с NativeAOT. Потокобезопасен.
/// </remarks>
public sealed class PolymarketRiskManager : IMarketRiskManager, IDisposable
{
    private readonly ConcurrentDictionary<string, PolymarketRiskRule> rules = new();
    private PolymarketPriceStream? priceStream;
    private PolymarketPortfolioTracker? tracker;
    private double dailyLossAccumulator;
    private bool isDisposed;

    /// <summary>
    /// Глобальные лимиты портфеля.
    /// </summary>
    public PolymarketPortfolioLimits Limits { get; set; } = new();

    /// <summary>
    /// Режим автоматического исполнения — если true, генерирует сигналы на закрытие.
    /// По умолчанию false — только уведомления через событие.
    /// </summary>
    public bool AutoExecute { get; set; }

    /// <summary>
    /// Все правила.
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketRiskRule> Rules => rules;

    /// <summary>
    /// Накопленный дневной убыток.
    /// </summary>
    public double DailyLoss => dailyLossAccumulator;

    /// <summary>
    /// Событие при срабатывании стопа.
    /// </summary>
    public event AsyncEventHandler<PolymarketRiskManager, PolymarketRiskTriggeredEventArgs>? RiskTriggered;

    /// <summary>
    /// Добавляет правило для актива.
    /// </summary>
    public void AddRule(PolymarketRiskRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rules[rule.AssetId] = rule;
    }

    /// <summary>
    /// Удаляет правило.
    /// </summary>
    public void RemoveRule(string assetId) => rules.TryRemove(assetId, out _);

    /// <summary>
    /// Получает правило для актива.
    /// </summary>
    public PolymarketRiskRule? GetRule(string assetId)
    {
        rules.TryGetValue(assetId, out var rule);
        return rule;
    }

    // IMarketRiskManager — явная реализация
    IMarketPortfolioLimits IMarketRiskManager.Limits => Limits;
    void IMarketRiskManager.AddRule(IMarketRiskRule rule) => AddRule((PolymarketRiskRule)rule);
    IMarketRiskRule? IMarketRiskManager.GetRule(string assetId) => GetRule(assetId);

    /// <summary>
    /// Очищает все правила.
    /// </summary>
    public void ClearRules() => rules.Clear();

    /// <summary>
    /// Подключается к PriceStream для мониторинга цен.
    /// </summary>
    public void ConnectPriceStream(PolymarketPriceStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        priceStream = stream;
        stream.PriceUpdated += OnPriceUpdatedAsync;
    }

    /// <summary>
    /// Подключается к PortfolioTracker для проверки лимитов.
    /// </summary>
    public void ConnectTracker(PolymarketPortfolioTracker portfolioTracker)
    {
        ArgumentNullException.ThrowIfNull(portfolioTracker);
        tracker = portfolioTracker;
        portfolioTracker.PositionChanged += OnPositionChangedAsync;
    }

    /// <summary>
    /// Отключает все подписки.
    /// </summary>
    public void DisconnectAll()
    {
        if (priceStream is not null)
            priceStream.PriceUpdated -= OnPriceUpdatedAsync;
        if (tracker is not null)
            tracker.PositionChanged -= OnPositionChangedAsync;
        priceStream = null;
        tracker = null;
    }

    /// <summary>
    /// Проверяет, можно ли открыть новую позицию по данному активу.
    /// </summary>
    /// <param name="assetId">Актив.</param>
    /// <param name="quantity">Запрашиваемый объём (USDC).</param>
    /// <returns>true если позиция не нарушает лимиты.</returns>
    public bool CanOpenPosition(string assetId, double quantity)
    {
        // Проверка максимального размера позиции
        if (quantity > Limits.MaxPositionSize) return false;

        if (tracker is null) return true;

        var summary = tracker.GetSummary();

        // Проверка количества открытых позиций
        if (summary.OpenPositions >= Limits.MaxOpenPositions) return false;

        // Проверка суммарного убытка портфеля
        if (summary.TotalUnrealizedPnL + summary.TotalRealizedPnL < -Limits.MaxPortfolioLoss) return false;

        // Проверка дневного лимита убытков
        if (dailyLossAccumulator >= Limits.MaxDailyLoss) return false;

        // Проверка максимальной доли на один актив
        if (summary.TotalMarketValue > 0)
        {
            var positionShare = quantity / (summary.TotalMarketValue + quantity);
            if (positionShare > Limits.MaxPositionPercent) return false;
        }

        return true;
    }

    /// <summary>
    /// Вручную проверяет все правила по текущим ценам.
    /// </summary>
    public async ValueTask CheckAllRulesAsync()
    {
        if (priceStream is null) return;

        foreach (var (assetId, rule) in rules)
        {
            if (rule.IsTriggered) continue;

            var snap = priceStream.GetPrice(assetId);
            if (snap?.Midpoint is null) continue;

            if (!double.TryParse(snap.Midpoint, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var price))
                continue;

            await EvaluateRuleAsync(rule, price).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Сбрасывает дневной счётчик убытков.
    /// </summary>
    public void ResetDailyLoss() => Interlocked.Exchange(ref dailyLossAccumulator, 0);

    /// <summary>
    /// Обработчик обновления цен — проверяет стопы.
    /// </summary>
    private async ValueTask OnPriceUpdatedAsync(
        PolymarketPriceStream sender,
        PolymarketPriceUpdatedEventArgs e)
    {
        var assetId = e.Snapshot.AssetId;
        if (!rules.TryGetValue(assetId, out var rule) || rule.IsTriggered) return;

        if (e.Snapshot.Midpoint is null) return;
        if (!double.TryParse(e.Snapshot.Midpoint, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var price))
            return;

        await EvaluateRuleAsync(rule, price).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработчик изменения позиций — проверяет лимиты портфеля.
    /// </summary>
    private async ValueTask OnPositionChangedAsync(
        PolymarketPortfolioTracker sender,
        PolymarketPositionChangedEventArgs e)
    {
        var position = e.Position;

        // Проверяем MaxLossPerPosition
        if (rules.TryGetValue(position.AssetId, out var rule) && !rule.IsTriggered
            && rule.MaxLossPerPosition.HasValue && position.UnrealizedPnL < -rule.MaxLossPerPosition.Value)
        {
            await TriggerRuleAsync(rule, PolymarketRiskOrderType.StopLoss,
                rule.MaxLossPerPosition.Value, position.CurrentPrice).ConfigureAwait(false);
        }

        // Проверяем общий портфельный стоп
        if (tracker is not null)
        {
            var summary = tracker.GetSummary();
            var totalLoss = summary.TotalUnrealizedPnL + summary.TotalRealizedPnL;

            if (totalLoss < -Limits.MaxPortfolioLoss && RiskTriggered is not null)
            {
                await RiskTriggered.Invoke(this, new PolymarketRiskTriggeredEventArgs(
                    "PORTFOLIO", PolymarketRiskOrderType.StopLoss,
                    -Limits.MaxPortfolioLoss, totalLoss)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Оценивает правило для конкретной цены.
    /// </summary>
    private async ValueTask EvaluateRuleAsync(PolymarketRiskRule rule, double price)
    {
        // Обновляем high water mark для trailing stop
        if (price > rule.HighWaterMark)
            rule.HighWaterMark = price;

        // Stop-Loss
        if (rule.StopLossPrice.HasValue && price <= rule.StopLossPrice.Value)
        {
            await TriggerRuleAsync(rule, PolymarketRiskOrderType.StopLoss, rule.StopLossPrice.Value, price).ConfigureAwait(false);
            return;
        }

        // Take-Profit
        if (rule.TakeProfitPrice.HasValue && price >= rule.TakeProfitPrice.Value)
        {
            await TriggerRuleAsync(rule, PolymarketRiskOrderType.TakeProfit, rule.TakeProfitPrice.Value, price).ConfigureAwait(false);
            return;
        }

        // Trailing Stop
        if (rule.TrailingStopPercent.HasValue && rule.HighWaterMark > 0)
        {
            var trailingStopPrice = rule.HighWaterMark * (1.0 - rule.TrailingStopPercent.Value);
            if (price <= trailingStopPrice)
                await TriggerRuleAsync(rule, PolymarketRiskOrderType.TrailingStop, trailingStopPrice, price).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Срабатывание правила риска и уведомление.
    /// </summary>
    private async ValueTask TriggerRuleAsync(
        PolymarketRiskRule rule, PolymarketRiskOrderType orderType,
        double triggerPrice, double currentPrice)
    {
        rule.IsTriggered = true;
        if (RiskTriggered is not null)
            await RiskTriggered.Invoke(this, new PolymarketRiskTriggeredEventArgs(
                rule.AssetId, orderType, triggerPrice, currentPrice)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        DisconnectAll();
        GC.SuppressFinalize(this);
    }
}
