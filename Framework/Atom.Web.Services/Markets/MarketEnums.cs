namespace Atom.Web.Services.Markets;

/// <summary>
/// Сторона ордера/сделки.
/// </summary>
public enum TradeSide : byte
{
    /// <summary>Покупка.</summary>
    Buy,

    /// <summary>Продажа.</summary>
    Sell
}

/// <summary>
/// Действие торговой стратегии.
/// </summary>
public enum TradeAction : byte
{
    /// <summary>Удерживать — нет сигнала.</summary>
    Hold,

    /// <summary>Покупать.</summary>
    Buy,

    /// <summary>Продавать.</summary>
    Sell
}

/// <summary>
/// Статус ордера.
/// </summary>
public enum MarketOrderStatus : byte
{
    /// <summary>Открыт, ожидает исполнения.</summary>
    Open,

    /// <summary>Частично исполнен.</summary>
    PartiallyFilled,

    /// <summary>Полностью исполнен.</summary>
    Filled,

    /// <summary>Отменён.</summary>
    Cancelled,

    /// <summary>Отклонён.</summary>
    Rejected
}

/// <summary>
/// Причина изменения позиции.
/// </summary>
public enum PositionChangeReason : byte
{
    /// <summary>Сделка.</summary>
    Trade,

    /// <summary>Обновление цены.</summary>
    PriceUpdate,

    /// <summary>Резолюция рынка.</summary>
    MarketResolved,

    /// <summary>Ручная синхронизация.</summary>
    ManualSync
}

/// <summary>
/// Статус рынка.
/// </summary>
public enum MarketStatus : byte
{
    /// <summary>Активен.</summary>
    Active,

    /// <summary>Закрыт.</summary>
    Closed,

    /// <summary>Резолюция.</summary>
    Resolved,

    /// <summary>Аннулирован.</summary>
    Voided,

    /// <summary>Неизвестен.</summary>
    Unknown
}

/// <summary>
/// Условие алерта.
/// </summary>
public enum AlertCondition : byte
{
    /// <summary>P&amp;L позиции.</summary>
    PnLThreshold,

    /// <summary>Цена актива.</summary>
    PriceThreshold,

    /// <summary>P&amp;L портфеля.</summary>
    PortfolioPnLThreshold,

    /// <summary>Рынок закрыт.</summary>
    MarketClosed,

    /// <summary>Рынок разрешён.</summary>
    MarketResolved
}

/// <summary>
/// Направление алерта.
/// </summary>
public enum AlertDirection : byte
{
    /// <summary>Выше порога.</summary>
    Above,

    /// <summary>Ниже порога.</summary>
    Below
}

/// <summary>
/// Тип ордера риск-менеджмента.
/// </summary>
public enum RiskOrderType : byte
{
    /// <summary>Stop-Loss.</summary>
    StopLoss,

    /// <summary>Take-Profit.</summary>
    TakeProfit,

    /// <summary>Trailing Stop.</summary>
    TrailingStop
}

/// <summary>
/// Формат экспорта данных.
/// </summary>
public enum ExportFormat : byte
{
    /// <summary>CSV.</summary>
    Csv,

    /// <summary>JSON.</summary>
    Json
}
