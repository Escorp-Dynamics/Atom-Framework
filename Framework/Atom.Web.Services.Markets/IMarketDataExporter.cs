namespace Atom.Web.Services.Markets;

/// <summary>
/// Экспорт торговых данных в CSV/JSON.
/// </summary>
public interface IMarketDataExporter
{
    /// <summary>
    /// Экспортирует позиции в строку.
    /// </summary>
    /// <param name="positions">Позиции.</param>
    /// <param name="format">Формат экспорта.</param>
    string ExportPositions(IEnumerable<IMarketPosition> positions, ExportFormat format);

    /// <summary>
    /// Экспортирует позиции в файл.
    /// </summary>
    void ExportPositionsToFile(IEnumerable<IMarketPosition> positions, ExportFormat format, string filePath);

    /// <summary>
    /// Экспортирует историю P&amp;L в строку.
    /// </summary>
    string ExportPnLHistory(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format);

    /// <summary>
    /// Экспортирует историю P&amp;L в файл.
    /// </summary>
    void ExportPnLHistoryToFile(IEnumerable<IMarketPnLSnapshot> snapshots, ExportFormat format, string filePath);
}
