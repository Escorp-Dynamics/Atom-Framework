using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class PageNavigationStateLogs
{
    [LoggerMessage(EventId = 1850, Level = LogLevel.Debug, Message = "Локальный транспорт принял мостовую команду {Command} для окна {WindowId} и вкладки {TabId}")]
    public static partial void LogPageTransportRequestReceived(this ILogger logger, string command, string windowId, string tabId);

    [LoggerMessage(EventId = 1851, Level = LogLevel.Warning, Message = "Локальный транспорт отклонил мостовой запрос для окна {WindowId} и вкладки {TabId}, причина: {Reason}")]
    public static partial void LogPageTransportRequestRejected(this ILogger logger, string windowId, string tabId, string reason);

    [LoggerMessage(EventId = 1852, Level = LogLevel.Information, Message = "Локальная навигация выполнена, вид: {Kind}, адрес: {Url}, окно: {WindowId}, вкладка: {TabId}")]
    public static partial void LogPageTransportNavigationApplied(this ILogger logger, string kind, string url, string windowId, string tabId);

    [LoggerMessage(EventId = 1853, Level = LogLevel.Debug, Message = "Локальный транспорт выполняет мостовую команду выполнения сценария для окна {WindowId} и вкладки {TabId}")]
    public static partial void LogPageTransportExecuteScript(this ILogger logger, string windowId, string tabId);

    [LoggerMessage(EventId = 1854, Level = LogLevel.Trace, Message = "Локальный транспорт поставил в очередь события жизненного цикла для адреса {Url}, окно: {WindowId}, вкладка: {TabId}")]
    public static partial void LogPageTransportLifecycleEventsQueued(this ILogger logger, string url, string windowId, string tabId);

    [LoggerMessage(EventId = 1855, Level = LogLevel.Debug, Message = "Локальный транспорт зарегистрировал обратный вызов {CallbackPath} для окна {WindowId} и вкладки {TabId}")]
    public static partial void LogPageTransportCallbackSubscribed(this ILogger logger, string callbackPath, string windowId, string tabId);

    [LoggerMessage(EventId = 1856, Level = LogLevel.Debug, Message = "Локальный транспорт снял обратный вызов {CallbackPath} для окна {WindowId} и вкладки {TabId}")]
    public static partial void LogPageTransportCallbackUnSubscribed(this ILogger logger, string callbackPath, string windowId, string tabId);

    [LoggerMessage(EventId = 1857, Level = LogLevel.Trace, Message = "Локальный транспорт поставил в очередь обратный вызов {CallbackPath} с количеством аргументов {ArgumentCount} для окна {WindowId} и вкладки {TabId}")]
    public static partial void LogPageTransportCallbackQueued(this ILogger logger, string callbackPath, int argumentCount, string windowId, string tabId);

    [LoggerMessage(EventId = 1858, Level = LogLevel.Trace, Message = "Локальный транспорт не распознал сценарий обратного вызова для окна {WindowId} и вкладки {TabId}, причина: {Reason}")]
    public static partial void LogPageTransportCallbackSkipped(this ILogger logger, string windowId, string tabId, string reason);
}