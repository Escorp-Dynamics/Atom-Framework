using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Позиция пользователя по отдельному токену (исходу рынка).
/// Содержит количество, среднюю цену входа и расчёт P&amp;L.
/// </summary>
public sealed class PolymarketPosition : IMarketPosition
{
    /// <summary>
    /// Идентификатор токена (asset ID).
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// Идентификатор рынка (condition ID).
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Исход ("Yes" / "No").
    /// </summary>
    public string? Outcome { get; init; }

    /// <summary>
    /// Текущее количество токенов в позиции.
    /// Положительное = long, отрицательное — не поддерживается Polymarket (всегда >= 0).
    /// </summary>
    public double Quantity { get; set; }

    /// <summary>
    /// Средняя цена входа в позицию (средневзвешенная по объёму).
    /// </summary>
    public double AverageCostBasis { get; set; }

    /// <summary>
    /// Общая стоимость позиции по цене входа (Quantity × AverageCostBasis).
    /// </summary>
    public double TotalCost => Quantity * AverageCostBasis;

    /// <summary>
    /// Текущая рыночная цена токена (обновляется автоматически из PriceStream).
    /// </summary>
    public double CurrentPrice { get; set; }

    /// <summary>
    /// Текущая рыночная стоимость позиции (Quantity × CurrentPrice).
    /// </summary>
    public double MarketValue => Quantity * CurrentPrice;

    /// <summary>
    /// Нереализованная прибыль/убыток (MarketValue - TotalCost).
    /// </summary>
    public double UnrealizedPnL => MarketValue - TotalCost;

    /// <summary>
    /// Нереализованная прибыль/убыток в процентах от стоимости входа.
    /// </summary>
    public double UnrealizedPnLPercent => TotalCost != 0 ? (UnrealizedPnL / TotalCost) * 100 : 0;

    /// <summary>
    /// Реализованная прибыль/убыток (от закрытых частей позиции и разрешённых рынков).
    /// </summary>
    public double RealizedPnL { get; set; }

    /// <summary>
    /// Суммарная уплаченная комиссия по операциям с этим токеном.
    /// </summary>
    public double TotalFees { get; set; }

    /// <summary>
    /// Общее количество сделок по позиции.
    /// </summary>
    public int TradeCount { get; set; }

    /// <summary>
    /// Время открытия позиции (первая сделка, UNIX timestamp).
    /// </summary>
    public long OpenedAtTicks { get; set; }

    /// <summary>
    /// Время последнего обновления позиции (UNIX timestamp).
    /// </summary>
    public long LastUpdateTicks { get; set; }

    /// <summary>
    /// Позиция закрыта (количество = 0).
    /// </summary>
    public bool IsClosed => Quantity <= 0;
}

/// <summary>
/// Итоговая статистика портфеля пользователя.
/// </summary>
public sealed class PolymarketPortfolioSummary
    : IMarketPortfolioSummary
{
    /// <summary>
    /// Общее количество открытых позиций.
    /// </summary>
    public int OpenPositions { get; init; }

    /// <summary>
    /// Общее количество закрытых позиций.
    /// </summary>
    public int ClosedPositions { get; init; }

    /// <summary>
    /// Суммарная текущая стоимость всех открытых позиций.
    /// </summary>
    public double TotalMarketValue { get; init; }

    /// <summary>
    /// Суммарная стоимость входа всех открытых позиций.
    /// </summary>
    public double TotalCostBasis { get; init; }

    /// <summary>
    /// Суммарный нереализованный P&amp;L по всем открытым позициям.
    /// </summary>
    public double TotalUnrealizedPnL { get; init; }

    /// <summary>
    /// Суммарный реализованный P&amp;L по всем позициям.
    /// </summary>
    public double TotalRealizedPnL { get; init; }

    /// <summary>
    /// Суммарные комиссии.
    /// </summary>
    public double TotalFees { get; init; }

    /// <summary>
    /// Общий P&amp;L (реализованный + нереализованный - комиссии).
    /// </summary>
    public double NetPnL => TotalRealizedPnL + TotalUnrealizedPnL - TotalFees;
}

/// <summary>
/// Аргументы события обновления позиции.
/// </summary>
public sealed class PolymarketPositionChangedEventArgs(PolymarketPosition position, PolymarketPositionChangeReason reason) : EventArgs
{
    /// <summary>
    /// Обновлённая позиция.
    /// </summary>
    public PolymarketPosition Position { get; } = position;

    /// <summary>
    /// Причина обновления.
    /// </summary>
    public PolymarketPositionChangeReason Reason { get; } = reason;
}

/// <summary>
/// Причина обновления позиции.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PolymarketPositionChangeReason>))]
public enum PolymarketPositionChangeReason
{
    /// <summary>
    /// Исполнение сделки (покупка/продажа).
    /// </summary>
    Trade,

    /// <summary>
    /// Обновление рыночной цены (P&amp;L пересчитан).
    /// </summary>
    PriceUpdate,

    /// <summary>
    /// Разрешение рынка (выплата/списание).
    /// </summary>
    MarketResolved,

    /// <summary>
    /// Ручная корректировка (синхронизация с REST API).
    /// </summary>
    ManualSync
}
