using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class ShadowRootLogs
{
    [LoggerMessage(EventId = 1912, Level = LogLevel.Debug, Message = "Теневой корень вкладки {TabId} начал вычисление сценария, длина сценария: {ScriptLength}")]
    public static partial void LogWebShadowRootEvaluateStarting(this ILogger logger, string tabId, int scriptLength);

    [LoggerMessage(EventId = 1913, Level = LogLevel.Trace, Message = "Теневой корень вкладки {TabId} начал операцию {Operation}, селектор: {Selector}")]
    public static partial void LogWebShadowRootDomOperationStarting(this ILogger logger, string tabId, string operation, string selector);
}