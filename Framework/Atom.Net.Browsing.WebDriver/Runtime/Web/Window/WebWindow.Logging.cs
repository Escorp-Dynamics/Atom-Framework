using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class WebWindowLogs
{
    [LoggerMessage(EventId = 1870, Level = LogLevel.Information, Message = "Создано окно {WindowId}")]
    public static partial void LogWebWindowCreated(this ILogger logger, string windowId);

    [LoggerMessage(EventId = 1871, Level = LogLevel.Debug, Message = "В окно {WindowId} добавлена вкладка {TabId}")]
    public static partial void LogWebWindowPageOpened(this ILogger logger, string windowId, string tabId);

    [LoggerMessage(EventId = 1872, Level = LogLevel.Trace, Message = "Окно {WindowId} ретранслировало мостовое событие {EventType} для вкладки {TabId}")]
    public static partial void LogWebWindowBridgeEventRelayed(this ILogger logger, string windowId, string eventType, string tabId);

    [LoggerMessage(EventId = 1873, Level = LogLevel.Debug, Message = "Текущая вкладка окна {WindowId} переключена на {TabId}")]
    public static partial void LogWebWindowCurrentPageSwitched(this ILogger logger, string windowId, string tabId);

    [LoggerMessage(EventId = 1874, Level = LogLevel.Information, Message = "Освобождение окна {WindowId} завершено")]
    public static partial void LogWebWindowDisposed(this ILogger logger, string windowId);

    [LoggerMessage(EventId = 1875, Level = LogLevel.Debug, Message = "Окно {WindowId} начало очистку cookie во вкладках, количество вкладок: {PageCount}")]
    public static partial void LogWebWindowCookiesClearing(this ILogger logger, string windowId, int pageCount);

    [LoggerMessage(EventId = 1876, Level = LogLevel.Debug, Message = "Окно {WindowId} направило навигацию текущей вкладке {TabId}, адрес: {Url}, вид: {Kind}")]
    public static partial void LogWebWindowNavigationStarting(this ILogger logger, string windowId, string tabId, string url, string kind);

    [LoggerMessage(EventId = 1877, Level = LogLevel.Debug, Message = "Окно {WindowId} начало перезагрузку текущей вкладки {TabId}, адрес: {Url}")]
    public static partial void LogWebWindowReloadStarting(this ILogger logger, string windowId, string tabId, string url);

    [LoggerMessage(EventId = 1910, Level = LogLevel.Trace, Message = "Окно {WindowId} начало поиск вкладки по {QueryKind}: {Query}")]
    public static partial void LogWebWindowLookupStarting(this ILogger logger, string windowId, string queryKind, string query);

    [LoggerMessage(EventId = 1911, Level = LogLevel.Trace, Message = "Окно {WindowId} завершило поиск вкладки по {QueryKind}: {Query}, результат: {Result}")]
    public static partial void LogWebWindowLookupCompleted(this ILogger logger, string windowId, string queryKind, string query, string result);
}