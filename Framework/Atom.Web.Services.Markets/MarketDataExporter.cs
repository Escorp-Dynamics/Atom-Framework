using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// Экспорт данных: позиции и P&L в CSV/JSON.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Реализация экспортёра данных.
/// </summary>
public sealed class MarketDataExporter : IMarketDataExporter
{
    /// <inheritdoc />
    public string ExportPositions(IEnumerable<IMarketPosition> positions, ExportFormat format) =>
        format switch
        {
            ExportFormat.Csv => ExportPositionsCsv(positions),
            ExportFormat.Json => ExportPositionsJson(positions),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    /// <inheritdoc />
    public void ExportPositionsToFile(IEnumerable<IMarketPosition> positions, ExportFormat format, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        File.WriteAllText(filePath, ExportPositions(positions, format), Encoding.UTF8);
    }

    /// <inheritdoc />
    public string ExportPnLHistory(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format) =>
        format switch
        {
            ExportFormat.Csv => ExportPnLCsv(snapshots),
            ExportFormat.Json => ExportPnLJson(snapshots),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    /// <inheritdoc />
    public void ExportPnLHistoryToFile(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        File.WriteAllText(filePath, ExportPnLHistory(snapshots, format), Encoding.UTF8);
    }

    #region CSV

    private static string ExportPositionsCsv(IEnumerable<IMarketPosition> positions)
    {
        using var sb = new Atom.Text.ValueStringBuilder();
        sb.AppendLine("AssetId,Quantity,AverageCostBasis,CurrentPrice,MarketValue,UnrealizedPnL,UnrealizedPnLPercent,RealizedPnL,TotalFees,TradeCount,IsClosed");

        foreach (var pos in positions)
        {
            sb.Append(EscapeCsv(pos.AssetId));
            sb.Append(',');
            sb.Append(pos.Quantity.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.AverageCostBasis.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.CurrentPrice.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.MarketValue.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.UnrealizedPnL.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.UnrealizedPnLPercent.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.RealizedPnL.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.TotalFees.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pos.TradeCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(pos.IsClosed ? "true" : "false");
        }

        return sb.ToString();
    }

    private static string ExportPnLCsv(IEnumerable<IMarketPnLSnapshot> snapshots)
    {
        using var sb = new Atom.Text.ValueStringBuilder();
        sb.AppendLine("Timestamp,TotalMarketValue,UnrealizedPnL,RealizedPnL,TotalFees,NetPnL");

        foreach (var snap in snapshots)
        {
            sb.Append(snap.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(snap.TotalMarketValue.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(snap.UnrealizedPnL.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(snap.RealizedPnL.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(snap.TotalFees.ToString("G", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(snap.NetPnL.ToString("G", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    #endregion

    #region JSON

    private static string ExportPositionsJson(IEnumerable<IMarketPosition> positions)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (var pos in positions)
        {
            writer.WriteStartObject();
            writer.WriteString("assetId", pos.AssetId);
            writer.WriteNumber("quantity", pos.Quantity);
            writer.WriteNumber("averageCostBasis", pos.AverageCostBasis);
            writer.WriteNumber("currentPrice", pos.CurrentPrice);
            writer.WriteNumber("marketValue", pos.MarketValue);
            writer.WriteNumber("unrealizedPnL", pos.UnrealizedPnL);
            writer.WriteNumber("unrealizedPnLPercent", pos.UnrealizedPnLPercent);
            writer.WriteNumber("realizedPnL", pos.RealizedPnL);
            writer.WriteNumber("totalFees", pos.TotalFees);
            writer.WriteNumber("tradeCount", pos.TradeCount);
            writer.WriteBoolean("isClosed", pos.IsClosed);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ExportPnLJson(IEnumerable<IMarketPnLSnapshot> snapshots)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (var snap in snapshots)
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", snap.Timestamp);
            writer.WriteNumber("totalMarketValue", snap.TotalMarketValue);
            writer.WriteNumber("unrealizedPnL", snap.UnrealizedPnL);
            writer.WriteNumber("realizedPnL", snap.RealizedPnL);
            writer.WriteNumber("totalFees", snap.TotalFees);
            writer.WriteNumber("netPnL", snap.NetPnL);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    #endregion
}
