namespace Atom.Web.Services.Markets;

/// <summary>
/// Трекер портфеля — отслеживает позиции, считает P&amp;L.
/// </summary>
public interface IMarketPortfolioTracker : IDisposable
{
    /// <summary>Количество открытых позиций.</summary>
    int OpenPositionCount { get; }

    /// <summary>
    /// Получает позицию по активу.
    /// </summary>
    IMarketPosition? GetPosition(string assetId);

    /// <summary>
    /// Получает сводку портфеля.
    /// </summary>
    IMarketPortfolioSummary GetSummary();

    /// <summary>
    /// Очищает все позиции.
    /// </summary>
    void ClearPositions();
}

/// <summary>
/// История P&amp;L — периодические снимки состояния портфеля.
/// </summary>
public interface IMarketPnLHistory : IDisposable
{
    /// <summary>Количество снимков.</summary>
    int Count { get; }

    /// <summary>Последний снимок.</summary>
    IMarketPnLSnapshot? Latest { get; }

    /// <summary>Идёт ли запись.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Делает снимок текущего состояния.
    /// </summary>
    IMarketPnLSnapshot TakeSnapshot();

    /// <summary>
    /// Запускает периодическую запись.
    /// </summary>
    void Start();

    /// <summary>
    /// Останавливает запись.
    /// </summary>
    ValueTask StopAsync();

    /// <summary>
    /// Очищает историю.
    /// </summary>
    void Clear();
}
