using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class ObscuraServerLogs
{
    [LoggerMessage(EventId = 1940, Level = LogLevel.Information, Message = "Obscura запущена: процесс {ProcessId}, CDP {Endpoint}")]
    public static partial void LogObscuraServerStarted(this ILogger logger, int processId, string endpoint);

    [LoggerMessage(EventId = 1941, Level = LogLevel.Warning, Message = "Obscura: обработчик '{Kind}' вкладки {TabId} завершился ошибкой")]
    public static partial void LogObscuraHandlerFailed(this ILogger logger, Exception exception, string kind, string tabId);

    [LoggerMessage(EventId = 1942, Level = LogLevel.Warning, Message = "Obscura: решение '{Action}' для callback '{Name}' вкладки {TabId} не применено — движок не умеет подменять вызов, исходная функция выполнена как есть")]
    public static partial void LogObscuraCallbackDecisionIgnored(this ILogger logger, string action, string name, string tabId);
}
