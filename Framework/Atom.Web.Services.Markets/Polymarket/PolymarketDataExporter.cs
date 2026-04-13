using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Формат экспорта данных.
/// </summary>
public enum PolymarketExportFormat : byte
{
    /// <summary>CSV (comma-separated values).</summary>
    Csv,

    /// <summary>JSON (через source-generated сериализатор).</summary>
    Json
}

/// <summary>
/// Экспортирует данные Polymarket (позиции, P&amp;L историю, сделки) в CSV и JSON.
/// Совместим с NativeAOT — использует source-generated JSON сериализацию.
/// </summary>
/// <remarks>
/// Потокобезопасен. Все методы — чистые функции без побочных эффектов (кроме записи в файл).
/// </remarks>
public sealed class PolymarketDataExporter
    : IMarketDataExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    #region Позиции — CSV

    /// <summary>
    /// Экспортирует позиции в CSV-строку.
    /// </summary>
    public string ExportPositionsCsvString(IEnumerable<PolymarketPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        using var sb = new Atom.Text.ValueStringBuilder();
        sb.AppendLine("AssetId,Market,Outcome,Quantity,AverageCostBasis,TotalCost,CurrentPrice,MarketValue,UnrealizedPnL,UnrealizedPnLPercent,RealizedPnL,TotalFees,TradeCount,IsClosed");

        foreach (var p in positions)
        {
            sb.Append(CsvEscape(p.AssetId)).Append(',');
            sb.Append(CsvEscape(p.Market ?? "")).Append(',');
            sb.Append(CsvEscape(p.Outcome ?? "")).Append(',');
            sb.Append(p.Quantity.ToString("G", Inv)).Append(',');
            sb.Append(p.AverageCostBasis.ToString("G", Inv)).Append(',');
            sb.Append(p.TotalCost.ToString("G", Inv)).Append(',');
            sb.Append(p.CurrentPrice.ToString("G", Inv)).Append(',');
            sb.Append(p.MarketValue.ToString("G", Inv)).Append(',');
            sb.Append(p.UnrealizedPnL.ToString("G", Inv)).Append(',');
            sb.Append(p.UnrealizedPnLPercent.ToString("G", Inv)).Append(',');
            sb.Append(p.RealizedPnL.ToString("G", Inv)).Append(',');
            sb.Append(p.TotalFees.ToString("G", Inv)).Append(',');
            sb.Append(p.TradeCount.ToString(Inv)).Append(',');
            sb.AppendLine(p.IsClosed ? "true" : "false");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Экспортирует позиции в CSV-файл.
    /// </summary>
    public void ExportPositionsCsv(IEnumerable<PolymarketPosition> positions, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportPositionsCsvString(positions), Encoding.UTF8);
    }

    #endregion

    #region Позиции — JSON

    /// <summary>
    /// Экспортирует позиции в JSON-строку (NativeAOT-совместимо).
    /// </summary>
    public string ExportPositionsJsonString(IEnumerable<PolymarketPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var array = positions is PolymarketPosition[] arr ? arr : positions.ToArray();
        return JsonSerializer.Serialize(array, PolymarketExportJsonContext.Default.PolymarketPositionArray);
    }

    /// <summary>
    /// Экспортирует позиции в JSON-файл.
    /// </summary>
    public void ExportPositionsJson(IEnumerable<PolymarketPosition> positions, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportPositionsJsonString(positions), Encoding.UTF8);
    }

    #endregion

    #region P&amp;L история — CSV

    /// <summary>
    /// Экспортирует историю P&amp;L в CSV-строку.
    /// </summary>
    public string ExportPnLHistoryCsvString(IEnumerable<PolymarketPnLSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        using var sb = new Atom.Text.ValueStringBuilder();
        sb.AppendLine("Timestamp,TotalMarketValue,TotalCostBasis,UnrealizedPnL,RealizedPnL,TotalFees,NetPnL,OpenPositions");

        foreach (var s in snapshots)
        {
            sb.Append(s.Timestamp.ToString("o", Inv)).Append(',');
            sb.Append(s.TotalMarketValue.ToString("G", Inv)).Append(',');
            sb.Append(s.TotalCostBasis.ToString("G", Inv)).Append(',');
            sb.Append(s.UnrealizedPnL.ToString("G", Inv)).Append(',');
            sb.Append(s.RealizedPnL.ToString("G", Inv)).Append(',');
            sb.Append(s.TotalFees.ToString("G", Inv)).Append(',');
            sb.Append(s.NetPnL.ToString("G", Inv)).Append(',');
            sb.AppendLine(s.OpenPositions.ToString(Inv));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Экспортирует историю P&amp;L в CSV-файл.
    /// </summary>
    public void ExportPnLHistoryCsv(IEnumerable<PolymarketPnLSnapshot> snapshots, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportPnLHistoryCsvString(snapshots), Encoding.UTF8);
    }

    #endregion

    #region P&amp;L история — JSON

    /// <summary>
    /// Экспортирует историю P&amp;L в JSON-строку.
    /// </summary>
    public string ExportPnLHistoryJsonString(IEnumerable<PolymarketPnLSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var array = snapshots is PolymarketPnLSnapshot[] arr ? arr : snapshots.ToArray();
        return JsonSerializer.Serialize(array, PolymarketExportJsonContext.Default.PolymarketPnLSnapshotArray);
    }

    /// <summary>
    /// Экспортирует историю P&amp;L в JSON-файл.
    /// </summary>
    public void ExportPnLHistoryJson(IEnumerable<PolymarketPnLSnapshot> snapshots, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportPnLHistoryJsonString(snapshots), Encoding.UTF8);
    }

    #endregion

    #region Сделки — CSV

    /// <summary>
    /// Экспортирует сделки в CSV-строку.
    /// </summary>
    public string ExportTradesCsvString(IEnumerable<PolymarketTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        using var sb = new Atom.Text.ValueStringBuilder();
        sb.AppendLine("Id,Market,AssetId,Side,Size,Price,FeeRateBps,Status,MatchTime,Outcome,TraderSide,Owner");

        foreach (var t in trades)
        {
            sb.Append(CsvEscape(t.Id ?? "")).Append(',');
            sb.Append(CsvEscape(t.Market ?? "")).Append(',');
            sb.Append(CsvEscape(t.AssetId ?? "")).Append(',');
            sb.Append(t.Side.ToString()).Append(',');
            sb.Append(CsvEscape(t.Size ?? "")).Append(',');
            sb.Append(CsvEscape(t.Price ?? "")).Append(',');
            sb.Append(CsvEscape(t.FeeRateBps ?? "")).Append(',');
            sb.Append(t.Status.ToString()).Append(',');
            sb.Append(CsvEscape(t.MatchTime ?? "")).Append(',');
            sb.Append(CsvEscape(t.Outcome ?? "")).Append(',');
            sb.Append(t.TraderSide.ToString()).Append(',');
            sb.AppendLine(CsvEscape(t.Owner ?? ""));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Экспортирует сделки в CSV-файл.
    /// </summary>
    public void ExportTradesCsv(IEnumerable<PolymarketTrade> trades, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportTradesCsvString(trades), Encoding.UTF8);
    }

    #endregion

    #region Сделки — JSON

    /// <summary>
    /// Экспортирует сделки в JSON-строку.
    /// </summary>
    public string ExportTradesJsonString(IEnumerable<PolymarketTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var array = trades is PolymarketTrade[] arr ? arr : trades.ToArray();
        return JsonSerializer.Serialize(array, PolymarketExportJsonContext.Default.PolymarketTradeArray);
    }

    /// <summary>
    /// Экспортирует сделки в JSON-файл.
    /// </summary>
    public void ExportTradesJson(IEnumerable<PolymarketTrade> trades, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportTradesJsonString(trades), Encoding.UTF8);
    }

    #endregion

    #region Полный отчёт портфеля — JSON

    /// <summary>
    /// Экспортирует полный отчёт портфеля (позиции + сводка + P&amp;L история) в JSON-строку.
    /// </summary>
    public string ExportPortfolioReportJsonString(
        PolymarketPortfolioTracker tracker,
        PolymarketPnLHistory? pnlHistory = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        var summary = tracker.GetSummary();
        var report = new PolymarketPortfolioReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Summary = summary,
            Positions = [.. tracker.Positions.Values],
            PnLHistory = pnlHistory?.ToArray() ?? []
        };

        return JsonSerializer.Serialize(report, PolymarketExportJsonContext.Default.PolymarketPortfolioReport);
    }

    /// <summary>
    /// Экспортирует полный отчёт портфеля в JSON-файл.
    /// </summary>
    public void ExportPortfolioReportJson(
        PolymarketPortfolioTracker tracker,
        PolymarketPnLHistory? pnlHistory,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        File.WriteAllText(filePath, ExportPortfolioReportJsonString(tracker, pnlHistory), Encoding.UTF8);
    }

    #endregion

    #region IMarketDataExporter — явная реализация

    string IMarketDataExporter.ExportPositions(IEnumerable<IMarketPosition> positions, ExportFormat format) =>
        format == ExportFormat.Json
            ? ExportPositionsJsonString(positions.Cast<PolymarketPosition>())
            : ExportPositionsCsvString(positions.Cast<PolymarketPosition>());

    void IMarketDataExporter.ExportPositionsToFile(IEnumerable<IMarketPosition> positions, ExportFormat format, string filePath)
    {
        if (format == ExportFormat.Json)
            ExportPositionsJson(positions.Cast<PolymarketPosition>(), filePath);
        else
            ExportPositionsCsv(positions.Cast<PolymarketPosition>(), filePath);
    }

    string IMarketDataExporter.ExportPnLHistory(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format) =>
        format == ExportFormat.Json
            ? ExportPnLHistoryJsonString(snapshots.Cast<PolymarketPnLSnapshot>())
            : ExportPnLHistoryCsvString(snapshots.Cast<PolymarketPnLSnapshot>());

    void IMarketDataExporter.ExportPnLHistoryToFile(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format, string filePath)
    {
        if (format == ExportFormat.Json)
            ExportPnLHistoryJson(snapshots.Cast<PolymarketPnLSnapshot>(), filePath);
        else
            ExportPnLHistoryCsv(snapshots.Cast<PolymarketPnLSnapshot>(), filePath);
    }

    #endregion

    #region CSV утилиты

    /// <summary>
    /// Экранирует значение для CSV (RFC 4180).
    /// </summary>
    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return string.Concat("\"", value.Replace("\"", "\"\""), "\"");
        return value;
    }

    #endregion
}

/// <summary>
/// Отчёт портфеля для экспорта в JSON.
/// </summary>
public sealed class PolymarketPortfolioReport
{
    /// <summary>
    /// Время генерации отчёта (UTC).
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// Сводная статистика портфеля.
    /// </summary>
    public required PolymarketPortfolioSummary Summary { get; init; }

    /// <summary>
    /// Все позиции портфеля.
    /// </summary>
    public required PolymarketPosition[] Positions { get; init; }

    /// <summary>
    /// История снимков P&amp;L.
    /// </summary>
    public required PolymarketPnLSnapshot[] PnLHistory { get; init; }
}

/// <summary>
/// Source-generated контекст JSON для экспорта данных.
/// Обеспечивает совместимость с NativeAOT.
/// </summary>
[JsonSerializable(typeof(PolymarketPosition[]))]
[JsonSerializable(typeof(PolymarketPnLSnapshot[]))]
[JsonSerializable(typeof(PolymarketTrade[]))]
[JsonSerializable(typeof(PolymarketPortfolioReport))]
[JsonSerializable(typeof(PolymarketPortfolioSummary))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class PolymarketExportJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
