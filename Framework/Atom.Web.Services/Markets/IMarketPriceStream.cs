namespace Atom.Web.Services.Markets;

/// <summary>
/// Стриминг цен в реальном времени — кеш и уведомления об обновлениях.
/// </summary>
public interface IMarketPriceStream : IDisposable
{
    /// <summary>
    /// Количество отслеживаемых активов.
    /// </summary>
    int TokenCount { get; }

    /// <summary>
    /// Получает текущий снимок цены для актива.
    /// </summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <returns>Снимок цены или null если актив не отслеживается.</returns>
    IMarketPriceSnapshot? GetPrice(string assetId);

    /// <summary>
    /// Очищает кеш цен.
    /// </summary>
    void ClearCache();
}
