using Microsoft.Extensions.Logging;

namespace Atom.Web.Browsing;

internal static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Information, "Процесс браузера запущен", SkipEnabledCheck = true)]
    public static partial void BrowserProcessRunning(this ILogger logger);

    [LoggerMessage(2, LogLevel.Trace, "{Info}", SkipEnabledCheck = true)]
    public static partial void BrowserProcessTrace(this ILogger logger, string info);

    [LoggerMessage(3, LogLevel.Debug, "Создана новая сессия: {Url}", SkipEnabledCheck = true)]
    public static partial void SessionConnected(this ILogger logger, Uri url);
}