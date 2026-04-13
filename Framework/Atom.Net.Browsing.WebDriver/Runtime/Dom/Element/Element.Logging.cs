using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class ElementLogs
{
    [LoggerMessage(EventId = 1894, Level = LogLevel.Debug, Message = "Элемент {Handle} во вкладке {TabId} начал доверенный клик")]
    public static partial void LogWebElementClickStarting(this ILogger logger, string handle, string tabId);

    [LoggerMessage(EventId = 1895, Level = LogLevel.Trace, Message = "Элемент {Handle} во вкладке {TabId} начал наведение")]
    public static partial void LogWebElementHoverStarting(this ILogger logger, string handle, string tabId);

    [LoggerMessage(EventId = 1896, Level = LogLevel.Trace, Message = "Элемент {Handle} во вкладке {TabId} начал установку фокуса")]
    public static partial void LogWebElementFocusStarting(this ILogger logger, string handle, string tabId);

    [LoggerMessage(EventId = 1897, Level = LogLevel.Debug, Message = "Элемент {Handle} во вкладке {TabId} начал доверенный ввод, длина текста: {TextLength}")]
    public static partial void LogWebElementTypeStarting(this ILogger logger, string handle, string tabId, int textLength);

    [LoggerMessage(EventId = 1898, Level = LogLevel.Trace, Message = "Элемент {Handle} во вкладке {TabId} начал нажатие клавиши {Key}")]
    public static partial void LogWebElementPressStarting(this ILogger logger, string handle, string tabId, string key);

    [LoggerMessage(EventId = 1899, Level = LogLevel.Trace, Message = "Элемент {Handle} во вкладке {TabId} начал получение теневого корня")]
    public static partial void LogWebElementShadowRootLookupStarting(this ILogger logger, string handle, string tabId);

    [LoggerMessage(EventId = 1914, Level = LogLevel.Trace, Message = "Элемент {Handle} во вкладке {TabId} перешёл на резервный DOM, корневой тег: {TagName}")]
    public static partial void LogWebElementFallbackActivated(this ILogger logger, string handle, string tabId, string tagName);
}