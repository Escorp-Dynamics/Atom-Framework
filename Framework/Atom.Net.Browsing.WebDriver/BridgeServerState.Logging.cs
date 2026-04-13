using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class BridgeServerStateLogs
{
    [LoggerMessage(EventId = 1820, Level = LogLevel.Information, Message = "Состояние мостового сервера удалило сеанс {SessionId}, вкладок: {RemovedTabCount}, завершённых запросов с ошибкой: {FailedPendingRequestCount}")]
    public static partial void LogBridgeServerStateSessionRemoved(this ILogger logger, string sessionId, int removedTabCount, int failedPendingRequestCount);

    [LoggerMessage(EventId = 1821, Level = LogLevel.Information, Message = "Состояние мостового сервера удалило вкладку {TabId} сеанса {SessionId}, завершённых запросов с ошибкой: {FailedPendingRequestCount}")]
    public static partial void LogBridgeServerStateTabRemoved(this ILogger logger, string sessionId, string tabId, int failedPendingRequestCount);
}