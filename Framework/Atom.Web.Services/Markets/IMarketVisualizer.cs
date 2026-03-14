namespace Atom.Web.Services.Markets;

/// <summary>
/// Визуализация торговых данных — ASCII-графики, таблицы, отчёты.
/// </summary>
public interface IMarketVisualizer
{
    /// <summary>Ширина графика (символы).</summary>
    int Width { get; set; }

    /// <summary>Высота графика (строки).</summary>
    int Height { get; set; }

    /// <summary>
    /// Рендерит ASCII-график значений.
    /// </summary>
    /// <param name="values">Массив значений.</param>
    /// <param name="title">Заголовок графика.</param>
    string RenderChart(double[] values, string title = "");

    /// <summary>
    /// Рендерит equity curve из результата бэктеста.
    /// </summary>
    string RenderEquityCurve(IMarketBacktestResult result);

    /// <summary>
    /// Рендерит историю P&amp;L.
    /// </summary>
    string RenderPnLHistory(IMarketPnLSnapshot[] snapshots);

    /// <summary>
    /// Рендерит таблицу позиций.
    /// </summary>
    string RenderPositionsTable(IEnumerable<IMarketPosition> positions);

    /// <summary>
    /// Рендерит сводку бэктеста.
    /// </summary>
    string RenderBacktestSummary(IMarketBacktestResult result);

    /// <summary>
    /// Рендерит сводку портфеля.
    /// </summary>
    string RenderPortfolioSummary(IMarketPortfolioSummary summary);
}
