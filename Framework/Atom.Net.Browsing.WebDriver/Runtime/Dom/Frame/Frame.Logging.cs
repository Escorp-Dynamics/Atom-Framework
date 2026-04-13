using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class FrameLogs
{
    [LoggerMessage(EventId = 1890, Level = LogLevel.Trace, Message = "Фрейм вкладки {TabId} принял событие жизненного цикла {EventType}, адрес: {Url}")]
    public static partial void LogWebFrameLifecycleEventReceived(this ILogger logger, string tabId, string eventType, string url);

    [LoggerMessage(EventId = 1891, Level = LogLevel.Debug, Message = "Фрейм вкладки {TabId} начал вычисление сценария, длина сценария: {ScriptLength}")]
    public static partial void LogWebFrameEvaluateStarting(this ILogger logger, string tabId, int scriptLength);

    [LoggerMessage(EventId = 1892, Level = LogLevel.Trace, Message = "Фрейм вкладки {TabId} начал операцию {Operation}, селектор: {Selector}")]
    public static partial void LogWebFrameDomOperationStarting(this ILogger logger, string tabId, string operation, string selector);
}