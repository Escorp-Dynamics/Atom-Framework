using System.Globalization;
using System.Text;

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

        var sb = new StringBuilder();
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range == 0) range = 1;

        var chartWidth = Math.Min(Width, values.Length);
        var step = Math.Max(1, values.Length / chartWidth);

        if (title.Length > 0)
            sb.AppendLine($"  {title}");

        // Верхняя граница
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {max,10:F2} ┤");

        for (var row = Height - 1; row >= 0; row--)
        {
            var threshold = min + range * row / (Height - 1);
            sb.Append(CultureInfo.InvariantCulture, $"  {threshold,10:F2} │");

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
        var sb = new StringBuilder();
        sb.AppendLine("  ┌──────────────┬──────────┬──────────────┬──────────────┬──────────────┬────────┐");
        sb.AppendLine("  │ Asset        │ Quantity │ Avg Cost     │ Current      │ Unrealized   │ Status │");
        sb.AppendLine("  ├──────────────┼──────────┼──────────────┼──────────────┼──────────────┼────────┤");

        foreach (var pos in positions)
        {
            var status = pos.IsClosed ? "Closed" : "Open";
            var pnlSign = pos.UnrealizedPnL >= 0 ? "+" : "";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  │ {Truncate(pos.AssetId, 12),-12} │ {pos.Quantity,8:F4} │ {pos.AverageCostBasis,12:F2} │ {pos.CurrentPrice,12:F2} │ {pnlSign}{pos.UnrealizedPnL,11:F2} │ {status,-6} │");
        }

        sb.AppendLine("  └──────────────┴──────────┴──────────────┴──────────────┴──────────────┴────────┘");
        return sb.ToString();
    }

    /// <inheritdoc />
    public string RenderBacktestSummary(IMarketBacktestResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ═══ Backtest: {result.StrategyName} ═══");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Initial Balance:   {result.InitialBalance,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Final Balance:     {result.FinalBalance,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Net P&L:           {result.NetPnL,12:F2} ({result.ReturnPercent:F2}%)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Total Trades:      {result.TotalTrades,12}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Win Rate:          {result.WinRate,11:F1}%");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Sharpe Ratio:      {result.SharpeRatio,12:F3}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Max Drawdown:      {result.MaxDrawdownPercent,11:F2}%");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Profit Factor:     {result.ProfitFactor,12:F3}");
        return sb.ToString();
    }

    /// <inheritdoc />
    public string RenderPortfolioSummary(IMarketPortfolioSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("  ═══ Portfolio Summary ═══");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Open Positions:    {summary.OpenPositions,12}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Closed Positions:  {summary.ClosedPositions,12}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Market Value:      {summary.TotalMarketValue,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Cost Basis:        {summary.TotalCostBasis,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Unrealized P&L:    {summary.TotalUnrealizedPnL,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Realized P&L:      {summary.TotalRealizedPnL,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Total Fees:        {summary.TotalFees,12:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Net P&L:           {summary.NetPnL,12:F2}");
        return sb.ToString();
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : string.Concat(s.AsSpan(0, maxLen - 1), "…");
}
