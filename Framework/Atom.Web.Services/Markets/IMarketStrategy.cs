namespace Atom.Web.Services.Markets;

/// <summary>
/// Торговая стратегия — оценивает рыночные данные и генерирует сигналы.
/// </summary>
public interface IMarketStrategy : IDisposable
{
    /// <summary>Имя стратегии.</summary>
    string Name { get; }

    /// <summary>
    /// Оценивает текущую ситуацию и генерирует торговый сигнал.
    /// </summary>
    /// <param name="priceStream">Источник цен.</param>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <returns>Торговый сигнал.</returns>
    IMarketTradeSignal Evaluate(IMarketPriceStream priceStream, string assetId);

    /// <summary>
    /// Обновляет внутреннее состояние стратегии при получении новой цены.
    /// </summary>
    /// <param name="snapshot">Снимок цены.</param>
    void OnPriceUpdated(IMarketPriceSnapshot snapshot);
}
