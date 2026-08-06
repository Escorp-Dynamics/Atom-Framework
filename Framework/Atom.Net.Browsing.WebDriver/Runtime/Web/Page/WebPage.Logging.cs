using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class WebPageLogs
{
    [LoggerMessage(EventId = 1880, Level = LogLevel.Information, Message = "Создана вкладка {TabId} в окне {WindowId}")]
    public static partial void LogWebPageCreated(this ILogger logger, string tabId, string windowId);

    [LoggerMessage(EventId = 1881, Level = LogLevel.Trace, Message = "Вкладка {TabId} синхронизировала мостовое событие {EventType}")]
    public static partial void LogWebPageBridgeEventSynced(this ILogger logger, string tabId, string eventType);

    [LoggerMessage(EventId = 1882, Level = LogLevel.Information, Message = "Освобождение вкладки {TabId} завершено")]
    public static partial void LogWebPageDisposed(this ILogger logger, string tabId);

    [LoggerMessage(EventId = 1883, Level = LogLevel.Debug, Message = "Вкладка {TabId} приняла событие жизненного цикла {EventType}, адрес: {Url}")]
    public static partial void LogWebPageLifecycleEventReceived(this ILogger logger, string tabId, string eventType, string url);

    [LoggerMessage(EventId = 1884, Level = LogLevel.Debug, Message = "Вкладка {TabId} начала очистку всех cookie")]
    public static partial void LogWebPageCookiesClearing(this ILogger logger, string tabId);

    [LoggerMessage(EventId = 1885, Level = LogLevel.Debug, Message = "Вкладка {TabId} начала установку cookie, количество: {CookieCount}")]
    public static partial void LogWebPageCookiesSetting(this ILogger logger, string tabId, int cookieCount);

    [LoggerMessage(EventId = 1886, Level = LogLevel.Debug, Message = "Вкладка {TabId} начала навигацию, адрес: {Url}, вид: {Kind}")]
    public static partial void LogWebPageNavigationStarting(this ILogger logger, string tabId, string url, string kind);

    [LoggerMessage(EventId = 1887, Level = LogLevel.Debug, Message = "Вкладка {TabId} начала перезагрузку, адрес: {Url}")]
    public static partial void LogWebPageReloadStarting(this ILogger logger, string tabId, string url);

    [LoggerMessage(EventId = 1888, Level = LogLevel.Debug, Message = "Вкладка {TabId} подписалась на обратный вызов {CallbackPath}")]
    public static partial void LogWebPageCallbackSubscribed(this ILogger logger, string tabId, string callbackPath);

    [LoggerMessage(EventId = 1889, Level = LogLevel.Debug, Message = "Вкладка {TabId} отписалась от обратного вызова {CallbackPath}")]
    public static partial void LogWebPageCallbackUnSubscribed(this ILogger logger, string tabId, string callbackPath);

    [LoggerMessage(EventId = 1890, Level = LogLevel.Warning, Message = "Вкладка {TabId} получила bridge ScriptError: {Details}")]
    public static partial void LogWebPageScriptErrorReceived(this ILogger logger, string tabId, string details);

    [LoggerMessage(EventId = 1891, Level = LogLevel.Information, Message = "Закрытие вкладки {TabId} начато")]
    public static partial void LogWebPageClosing(this ILogger logger, string tabId);

    [LoggerMessage(EventId = 1892, Level = LogLevel.Debug, Message = "Вкладка {TabId} выполняет полную перенастройку окружения из новых настроек")]
    public static partial void LogWebPageReconfiguring(this ILogger logger, string tabId);

    [LoggerMessage(EventId = 1893, Level = LogLevel.Debug, Message = "Вкладка {TabId} завершила полную перенастройку окружения")]
    public static partial void LogWebPageReconfigured(this ILogger logger, string tabId);
}