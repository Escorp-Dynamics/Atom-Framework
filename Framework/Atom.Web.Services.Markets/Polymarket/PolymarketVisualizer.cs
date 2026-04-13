using System.Globalization;
using Atom.Text;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Генерирует ASCII-графики и текстовые отчёты для данных Polymarket.
/// Подходит для консоли, логов и Markdown.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Без внешних зависимостей.
/// </remarks>
public sealed class PolymarketVisualizer
    : IMarketVisualizer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Ширина графика в символах (по умолчанию 60).
    /// </summary>
    public int Width { get; set; } = 60;

    /// <summary>
    /// Высота графика в строках (по умолчанию 15).
    /// </summary>
    public int Height { get; set; } = 15;

    /// <summary>
    /// Рендерит equity curve в ASCII-арт.
    /// </summary>
    /// <param name="values">Массив значений (баланс, P&amp;L и т.п.).</param>
    /// <param name="title">Заголовок графика.</param>
    /// <returns>Многострочная строка с графиком.</returns>
    public string RenderChart(double[] values, string title = "")
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) return "[Нет данных]";

        using var sb = new ValueStringBuilder();

        if (!string.IsNullOrEmpty(title))
            sb.AppendLine(title);

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range == 0) range = 1;

        // Сэмплируем данные под ширину графика
        var sampled = SampleValues(values, Width);

        // Рисуем график снизу вверх
        for (int row = Height - 1; row >= 0; row--)
        {
            var threshold = min + (range * row / (Height - 1));
            sb.Append(FormatAxisLabel(threshold));
            sb.Append(" │");

            for (int col = 0; col < sampled.Length; col++)
            {
                var normalizedRow = (int)((sampled[col] - min) / range * (Height - 1));
                if (normalizedRow == row)
                    sb.Append('●');
                else if (normalizedRow > row)
                    sb.Append('│');
                else
                    sb.Append(' ');
            }

            sb.AppendLine();
        }

        // Ось X
        sb.Append("         └");
        sb.Append(new string('─', sampled.Length));
        sb.AppendLine();

        // Статистика
        sb.Append($"  Min: {min.ToString("F2", Inv)}  Max: {max.ToString("F2", Inv)}");
        sb.Append($"  Points: {values.Length}");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Рендерит equity curve из результата бэктеста.
    /// </summary>
    public string RenderEquityCurve(PolymarketBacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return RenderChart(result.EquityCurve, $"Equity Curve — {result.StrategyName}");
    }

    /// <summary>
    /// Рендерит историю P&amp;L из снимков.
    /// </summary>
    public string RenderPnLHistory(PolymarketPnLSnapshot[] snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var values = snapshots.Select(s => s.NetPnL).ToArray();
        return RenderChart(values, "P&amp;L History");
    }

    /// <summary>
    /// Рендерит таблицу позиций портфеля.
    /// </summary>
    public string RenderPositionsTable(IEnumerable<PolymarketPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var posArray = positions is PolymarketPosition[] arr ? arr : positions.ToArray();
        if (posArray.Length == 0) return "[Нет открытых позиций]";

        using var sb = new ValueStringBuilder();
        var header = "│ Asset              │ Qty      │ Cost     │ Price    │ P&amp;L      │ P&amp;L%     │";
        var separator = "├────────────────────┼──────────┼──────────┼──────────┼──────────┼──────────┤";
        var top =       "┌────────────────────┬──────────┬──────────┬──────────┬──────────┬──────────┐";
        var bottom =    "└────────────────────┴──────────┴──────────┴──────────┴──────────┴──────────┘";

        sb.AppendLine(top);
        sb.AppendLine(header);
        sb.AppendLine(separator);

        foreach (var p in posArray)
        {
            var asset = Truncate(p.AssetId, 18);
            sb.Append("│ ").Append(asset.PadRight(18)).Append(" │ ");
            sb.Append(p.Quantity.ToString("F2", Inv).PadLeft(8)).Append(" │ ");
            sb.Append(p.AverageCostBasis.ToString("F4", Inv).PadLeft(8)).Append(" │ ");
            sb.Append(p.CurrentPrice.ToString("F4", Inv).PadLeft(8)).Append(" │ ");
            sb.Append(FormatPnL(p.UnrealizedPnL).PadLeft(8)).Append(" │ ");
            sb.Append(FormatPercent(p.UnrealizedPnLPercent).PadLeft(8)).Append(" │");
            sb.AppendLine();
        }

        sb.AppendLine(bottom);
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит сводку бэктеста.
    /// </summary>
    public string RenderBacktestSummary(PolymarketBacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var sb = new ValueStringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════╗");
        sb.AppendFormat(Inv, "║  Бэктест: {0,-30} ║", result.StrategyName).AppendLine();
        sb.AppendLine("╠══════════════════════════════════════════╣");
        sb.AppendFormat(Inv, "║  Начальный баланс:  {0,16:F2}   ║", result.InitialBalance).AppendLine();
        sb.AppendFormat(Inv, "║  Итоговый баланс:   {0,16:F2}   ║", result.FinalBalance).AppendLine();
        sb.AppendFormat(Inv, "║  P&amp;L:               {0,16:F2}   ║", result.NetPnL).AppendLine();
        sb.AppendFormat(Inv, "║  Доходность:        {0,15:F1}%   ║", result.ReturnPercent).AppendLine();
        sb.AppendLine("╠══════════════════════════════════════════╣");
        sb.AppendFormat(Inv, "║  Сделок:            {0,16}   ║", result.TotalTrades).AppendLine();
        sb.AppendFormat(Inv, "║  Прибыльных:        {0,16}   ║", result.WinningTrades).AppendLine();
        sb.AppendFormat(Inv, "║  Убыточных:         {0,16}   ║", result.LosingTrades).AppendLine();
        sb.AppendFormat(Inv, "║  Win Rate:          {0,15:F1}%   ║", result.WinRate).AppendLine();
        sb.AppendLine("╠══════════════════════════════════════════╣");
        sb.AppendFormat(Inv, "║  Sharpe Ratio:      {0,16:F2}   ║", result.SharpeRatio).AppendLine();
        sb.AppendFormat(Inv, "║  Max Drawdown:      {0,15:F1}%   ║", result.MaxDrawdownPercent).AppendLine();
        sb.AppendFormat(Inv, "║  Profit Factor:     {0,16:F2}   ║", result.ProfitFactor).AppendLine();
        sb.AppendFormat(Inv, "║  Avg P&amp;L / Trade:   {0,16:F2}   ║", result.AveragePnLPerTrade).AppendLine();
        sb.AppendLine("╚══════════════════════════════════════════╝");
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит сводку портфеля.
    /// </summary>
    public string RenderPortfolioSummary(PolymarketPortfolioSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        using var sb = new ValueStringBuilder();
        sb.AppendLine("┌──────────────────────────────────────┐");
        sb.AppendLine("│          Сводка портфеля             │");
        sb.AppendLine("├──────────────────────────────────────┤");
        sb.AppendFormat(Inv, "│  Открыто:       {0,16}   │", summary.OpenPositions).AppendLine();
        sb.AppendFormat(Inv, "│  Закрыто:       {0,16}   │", summary.ClosedPositions).AppendLine();
        sb.AppendFormat(Inv, "│  Рын. стоимость:{0,16:F2}   │", summary.TotalMarketValue).AppendLine();
        sb.AppendFormat(Inv, "│  Базис:         {0,16:F2}   │", summary.TotalCostBasis).AppendLine();
        sb.AppendFormat(Inv, "│  Unrealized:    {0,16:F2}   │", summary.TotalUnrealizedPnL).AppendLine();
        sb.AppendFormat(Inv, "│  Realized:      {0,16:F2}   │", summary.TotalRealizedPnL).AppendLine();
        sb.AppendFormat(Inv, "│  Комиссии:      {0,16:F2}   │", summary.TotalFees).AppendLine();
        sb.AppendFormat(Inv, "│  Net P&amp;L:       {0,16:F2}   │", summary.NetPnL).AppendLine();
        sb.AppendLine("└──────────────────────────────────────┘");
        return sb.ToString();
    }

    #region Утилиты

    /// <summary>
    /// Сэмплирует массив до указанного количества точек.
    /// </summary>
    private static double[] SampleValues(double[] values, int targetCount)
    {
        if (values.Length <= targetCount) return values;

        var result = new double[targetCount];
        var step = (double)values.Length / targetCount;

        for (int i = 0; i < targetCount; i++)
        {
            var idx = (int)(i * step);
            result[i] = values[Math.Min(idx, values.Length - 1)];
        }

        return result;
    }

    /// <summary>
    /// Форматирует метку оси Y.
    /// </summary>
    private static string FormatAxisLabel(double value)
    {
        var formatted = value.ToString("F1", Inv);
        return formatted.PadLeft(8);
    }

    /// <summary>
    /// Форматирует P&amp;L со знаком.
    /// </summary>
    private static string FormatPnL(double value) =>
        value >= 0
            ? $"+{value.ToString("F2", Inv)}"
            : value.ToString("F2", Inv);

    /// <summary>
    /// Форматирует процент.
    /// </summary>
    private static string FormatPercent(double value) =>
        value >= 0
            ? $"+{value.ToString("F1", Inv)}%"
            : $"{value.ToString("F1", Inv)}%";

    /// <summary>
    /// Обрезает строку до указанной длины.
    /// </summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 2), "..");

    #endregion

    #region IMarketVisualizer — явная реализация

    string IMarketVisualizer.RenderEquityCurve(IMarketBacktestResult result) =>
        RenderEquityCurve((PolymarketBacktestResult)result);

    string IMarketVisualizer.RenderPnLHistory(IMarketPnLSnapshot[] snapshots) =>
        RenderPnLHistory(snapshots.Cast<PolymarketPnLSnapshot>().ToArray());

    string IMarketVisualizer.RenderPositionsTable(IEnumerable<IMarketPosition> positions) =>
        RenderPositionsTable(positions.Cast<PolymarketPosition>());

    string IMarketVisualizer.RenderBacktestSummary(IMarketBacktestResult result) =>
        RenderBacktestSummary((PolymarketBacktestResult)result);

    string IMarketVisualizer.RenderPortfolioSummary(IMarketPortfolioSummary summary) =>
        RenderPortfolioSummary((PolymarketPortfolioSummary)summary);

    #endregion
}
