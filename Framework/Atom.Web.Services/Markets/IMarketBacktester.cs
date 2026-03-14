namespace Atom.Web.Services.Markets;

/// <summary>
/// Движок бэктестирования стратегий.
/// </summary>
public interface IMarketBacktester
{
    /// <summary>Начальный баланс.</summary>
    double InitialBalance { get; set; }

    /// <summary>Комиссия (BPS).</summary>
    int FeeRateBps { get; set; }

    /// <summary>
    /// Прогоняет стратегию по историческим данным.
    /// </summary>
    /// <param name="strategy">Стратегия.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="priceData">Исторические ценовые точки.</param>
    /// <returns>Результат бэктеста.</returns>
    IMarketBacktestResult Run(IMarketStrategy strategy, string assetId, IMarketPricePoint[] priceData);
}
