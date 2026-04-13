using System.Globalization;
using Atom.Text;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// ASCII-визуализация: графики, таблицы позиций, отчёты бэктеста.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация ASCII-визуализатора.
/// </summary>
public sealed class MarketVisualizer : IMarketVisualizer
{
    /// <inheritdoc />
    public int Width { get; set; } = 60;

    /// <inheritdoc />
    public int Height { get; set; } = 15;

    /// <inheritdoc />
    public string RenderChart(double[] values, string title = "")
    {
        if (values.Length == 0)
            return title.Length > 0 ? $"  {title}\n  (нет данных)" : "  (нет данных)";

        using var sb = new ValueStringBuilder();
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range == 0) range = 1;

        var chartWidth = Math.Min(Width, values.Length);
        var step = Math.Max(1, values.Length / chartWidth);

        if (title.Length > 0)
            sb.AppendLine($"  {title}");

        // Верхняя граница
        sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,10:F2} ┤", max).AppendLine();

        for (var row = Height - 1; row >= 0; row--)
        {
            var threshold = min + range * row / (Height - 1);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,10:F2} │", threshold);

            for (var col = 0; col < chartWidth; col++)
            {
                var idx = col * step;
                if (idx >= values.Length) break;
                var val = values[idx];
                var normalizedRow = (int)((val - min) / range * (Height - 1));
                sb.Append(normalizedRow >= row ? '█' : ' ');
            }
            sb.AppendLine();
        }

        // Ось X
        sb.Append("             └");
        sb.Append('─', chartWidth);
        sb.AppendLine();

        return sb.ToString();
    }

    /// <inheritdoc />
    public string RenderEquityCurve(IMarketBacktestResult result) =>
        RenderChart(result.EquityCurve, $"Equity Curve — {result.StrategyName}");

    /// <inheritdoc />
    public string RenderPnLHistory(IMarketPnLSnapshot[] snapshots)
    {
        if (snapshots.Length == 0)
            return "  P&L History\n  (нет данных)";

        var values = new double[snapshots.Length];
        for (var i = 0; i < snapshots.Length; i++)
            values[i] = snapshots[i].NetPnL;

        return RenderChart(values, "P&L History");
    }

    /// <inheritdoc />
    public string RenderPositionsTable(IEnumerable<IMarketPosition> positions)
    {
        using var sb = new ValueStringBuilder();
        sb.AppendLine("  ┌──────────────┬──────────┬──────────────┬──────────────┬──────────────┬────────┐");
        sb.AppendLine("  │ Asset        │ Quantity │ Avg Cost     │ Current      │ Unrealized   │ Status │");
        sb.AppendLine("  ├──────────────┼──────────┼──────────────┼──────────────┼──────────────┼────────┤");

        foreach (var pos in positions)
        {
            var status = pos.IsClosed ? "Closed" : "Open";
            var pnlSign = pos.UnrealizedPnL >= 0 ? "+" : "";
            sb.Append("  │ ");
            sb.Append(Truncate(pos.AssetId, 12).PadRight(12));
            sb.Append(" │ ");
            sb.Append(pos.Quantity.ToString("F4", CultureInfo.InvariantCulture).PadLeft(8));
            sb.Append(" │ ");
            sb.Append(pos.AverageCostBasis.ToString("F2", CultureInfo.InvariantCulture).PadLeft(12));
            sb.Append(" │ ");
            sb.Append(pos.CurrentPrice.ToString("F2", CultureInfo.InvariantCulture).PadLeft(12));
            sb.Append(" │ ");
            sb.Append(pnlSign);
            sb.Append(pos.UnrealizedPnL.ToString("F2", CultureInfo.InvariantCulture).PadLeft(11));
            sb.Append(" │ ");
            sb.Append(status.PadRight(6));
            sb.Append(" │").AppendLine();
        }

        sb.AppendLine("  └──────────────┴──────────┴──────────────┴──────────────┴──────────────┴────────┘");
        return sb.ToString();
    }

    /// <inheritdoc />
    public string RenderBacktestSummary(IMarketBacktestResult result)
    {
        using var sb = new ValueStringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  ═══ Backtest: {0} ═══", result.StrategyName).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Initial Balance:   {0,12:F2}", result.InitialBalance).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Final Balance:     {0,12:F2}", result.FinalBalance).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Net P&L:           {0,12:F2} ({1:F2}%)", result.NetPnL, result.ReturnPercent).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Total Trades:      {0,12}", result.TotalTrades).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Win Rate:          {0,11:F1}%", result.WinRate).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Sharpe Ratio:      {0,12:F3}", result.SharpeRatio).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Max Drawdown:      {0,11:F2}%", result.MaxDrawdownPercent).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Profit Factor:     {0,12:F3}", result.ProfitFactor).AppendLine();
        return sb.ToString();
    }

    /// <inheritdoc />
    public string RenderPortfolioSummary(IMarketPortfolioSummary summary)
    {
        using var sb = new ValueStringBuilder();
        sb.AppendLine("  ═══ Portfolio Summary ═══");
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Open Positions:    {0,12}", summary.OpenPositions).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Closed Positions:  {0,12}", summary.ClosedPositions).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Market Value:      {0,12:F2}", summary.TotalMarketValue).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Cost Basis:        {0,12:F2}", summary.TotalCostBasis).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Unrealized P&L:    {0,12:F2}", summary.TotalUnrealizedPnL).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Realized P&L:      {0,12:F2}", summary.TotalRealizedPnL).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Total Fees:        {0,12:F2}", summary.TotalFees).AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "  Net P&L:           {0,12:F2}", summary.NetPnL).AppendLine();
        return sb.ToString();
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : string.Concat(s.AsSpan(0, maxLen - 1), "…");
}
