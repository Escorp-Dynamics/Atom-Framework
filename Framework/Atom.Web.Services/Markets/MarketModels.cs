namespace Atom.Web.Services.Markets;

/// <summary>
/// Снимок цены актива.
/// </summary>
public interface IMarketPriceSnapshot
{
    /// <summary>Идентификатор актива.</summary>
    string AssetId { get; }

    /// <summary>Лучшая цена покупки.</summary>
    double? BestBid { get; }

    /// <summary>Лучшая цена продажи.</summary>
    double? BestAsk { get; }

    /// <summary>Средняя цена (midpoint).</summary>
    double? Midpoint { get; }

    /// <summary>Цена последней сделки.</summary>
    double? LastTradePrice { get; }

    /// <summary>Время последнего обновления (ticks).</summary>
    long LastUpdateTicks { get; }
}

/// <summary>
/// Позиция на рынке.
/// </summary>
public interface IMarketPosition
{
    /// <summary>Идентификатор актива.</summary>
    string AssetId { get; }

    /// <summary>Количество.</summary>
    double Quantity { get; set; }

    /// <summary>Средневзвешенная цена входа.</summary>
    double AverageCostBasis { get; set; }

    /// <summary>Текущая цена актива.</summary>
    double CurrentPrice { get; set; }

    /// <summary>Рыночная стоимость.</summary>
    double MarketValue { get; }

    /// <summary>Нереализованный P&amp;L.</summary>
    double UnrealizedPnL { get; }

    /// <summary>Нереализованный P&amp;L в процентах.</summary>
    double UnrealizedPnLPercent { get; }

    /// <summary>Реализованный P&amp;L.</summary>
    double RealizedPnL { get; set; }

    /// <summary>Общие комиссии.</summary>
    double TotalFees { get; set; }

    /// <summary>Количество сделок.</summary>
    int TradeCount { get; set; }

    /// <summary>Закрыта ли позиция.</summary>
    bool IsClosed { get; }
}

/// <summary>
/// Сводка портфеля.
/// </summary>
public interface IMarketPortfolioSummary
{
    /// <summary>Открытые позиции.</summary>
    int OpenPositions { get; }

    /// <summary>Закрытые позиции.</summary>
    int ClosedPositions { get; }

    /// <summary>Суммарная рыночная стоимость.</summary>
    double TotalMarketValue { get; }

    /// <summary>Суммарный базис.</summary>
    double TotalCostBasis { get; }

    /// <summary>Суммарный нереализованный P&amp;L.</summary>
    double TotalUnrealizedPnL { get; }

    /// <summary>Суммарный реализованный P&amp;L.</summary>
    double TotalRealizedPnL { get; }

    /// <summary>Суммарные комиссии.</summary>
    double TotalFees { get; }

    /// <summary>Чистый P&amp;L.</summary>
    double NetPnL { get; }
}

/// <summary>
/// Торговый сигнал стратегии.
/// </summary>
public interface IMarketTradeSignal
{
    /// <summary>Идентификатор актива.</summary>
    string AssetId { get; }

    /// <summary>Действие.</summary>
    TradeAction Action { get; }

    /// <summary>Объём.</summary>
    double Quantity { get; }

    /// <summary>Цена (опционально).</summary>
    string? Price { get; }

    /// <summary>Уверенность сигнала (0.0–1.0).</summary>
    double Confidence { get; }

    /// <summary>Причина/обоснование.</summary>
    string? Reason { get; }
}

/// <summary>
/// Снимок P&amp;L для истории.
/// </summary>
public interface IMarketPnLSnapshot
{
    /// <summary>Временная метка.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>Суммарная рыночная стоимость.</summary>
    double TotalMarketValue { get; }

    /// <summary>Нереализованный P&amp;L.</summary>
    double UnrealizedPnL { get; }

    /// <summary>Реализованный P&amp;L.</summary>
    double RealizedPnL { get; }

    /// <summary>Суммарные комиссии.</summary>
    double TotalFees { get; }

    /// <summary>Чистый P&amp;L.</summary>
    double NetPnL { get; }
}

/// <summary>
/// Определение алерта.
/// </summary>
public interface IMarketAlertDefinition
{
    /// <summary>Уникальный идентификатор.</summary>
    string Id { get; }

    /// <summary>Условие.</summary>
    AlertCondition Condition { get; }

    /// <summary>Направление.</summary>
    AlertDirection Direction { get; }

    /// <summary>Пороговое значение.</summary>
    double Threshold { get; }

    /// <summary>Идентификатор актива (опционально).</summary>
    string? AssetId { get; }

    /// <summary>Описание.</summary>
    string? Description { get; }

    /// <summary>Одноразовый алерт.</summary>
    bool OneShot { get; }

    /// <summary>Активен.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Уже сработал.</summary>
    bool HasTriggered { get; set; }
}

/// <summary>
/// Правило риск-менеджмента.
/// </summary>
public interface IMarketRiskRule
{
    /// <summary>Идентификатор актива.</summary>
    string AssetId { get; }

    /// <summary>Stop-Loss цена.</summary>
    double? StopLossPrice { get; set; }

    /// <summary>Take-Profit цена.</summary>
    double? TakeProfitPrice { get; set; }

    /// <summary>Trailing stop (%).</summary>
    double? TrailingStopPercent { get; set; }

    /// <summary>Макс. убыток на позицию.</summary>
    double? MaxLossPerPosition { get; set; }

    /// <summary>Сработал ли.</summary>
    bool IsTriggered { get; set; }
}

/// <summary>
/// Лимиты портфеля.
/// </summary>
public interface IMarketPortfolioLimits
{
    /// <summary>Макс. размер позиции.</summary>
    double MaxPositionSize { get; set; }

    /// <summary>Макс. количество позиций.</summary>
    int MaxOpenPositions { get; set; }

    /// <summary>Макс. убыток портфеля.</summary>
    double MaxPortfolioLoss { get; set; }

    /// <summary>Макс. доля на один актив.</summary>
    double MaxPositionPercent { get; set; }

    /// <summary>Макс. дневной убыток.</summary>
    double MaxDailyLoss { get; set; }
}

/// <summary>
/// Результат бэктеста.
/// </summary>
public interface IMarketBacktestResult
{
    /// <summary>Имя стратегии.</summary>
    string StrategyName { get; }

    /// <summary>Начальный баланс.</summary>
    double InitialBalance { get; }

    /// <summary>Итоговый баланс.</summary>
    double FinalBalance { get; }

    /// <summary>Чистый P&amp;L.</summary>
    double NetPnL { get; }

    /// <summary>Доходность (%).</summary>
    double ReturnPercent { get; }

    /// <summary>Всего сделок.</summary>
    int TotalTrades { get; }

    /// <summary>Win Rate (%).</summary>
    double WinRate { get; }

    /// <summary>Sharpe Ratio.</summary>
    double SharpeRatio { get; }

    /// <summary>Макс. просадка (%).</summary>
    double MaxDrawdownPercent { get; }

    /// <summary>Profit Factor.</summary>
    double ProfitFactor { get; }

    /// <summary>Equity Curve.</summary>
    double[] EquityCurve { get; }
}

/// <summary>
/// Историческая ценовая точка для бэктеста.
/// </summary>
public interface IMarketPricePoint
{
    /// <summary>Цена midpoint.</summary>
    double Midpoint { get; }

    /// <summary>Best bid.</summary>
    double? BestBid { get; }

    /// <summary>Best ask.</summary>
    double? BestAsk { get; }

    /// <summary>Временная метка.</summary>
    DateTimeOffset Timestamp { get; }
}
