using Microsoft.Extensions.Logging;

namespace Atom.Hardware.Display;

internal static partial class VirtualDisplayLogs
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Information, Message = "Запуск виртуального дисплея {Display} с разрешением {Width}x{Height}x{Depth}. Видимый режим: {IsVisible}.")]
    public static partial void LogVirtualDisplayStarting(this ILogger logger, string display, int width, int height, int depth, bool isVisible);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Debug, Message = "X11 socket дисплея {Display} готов.")]
    public static partial void LogVirtualDisplayX11SocketReady(this ILogger logger, string display);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "xpra control socket для дисплея {Display} опубликован. {SocketDiagnostics}")]
    public static partial void LogVirtualDisplayControlSocketReady(this ILogger logger, string display, string socketDiagnostics);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning, Message = "Для дисплея {Display} не удалось запустить оконный менеджер. Продолжаю без WM.")]
    public static partial void LogVirtualDisplayWindowManagerUnavailable(this ILogger logger, string display);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Debug, Message = "Для дисплея {Display} запущен оконный менеджер {WindowManagerName}.")]
    public static partial void LogVirtualDisplayWindowManagerStarted(this ILogger logger, string display, string windowManagerName);

    [LoggerMessage(EventId = 1405, Level = LogLevel.Information, Message = "xpra attach для дисплея {Display} подключён к хостовому сеансу.")]
    public static partial void LogVirtualDisplayAttachConnected(this ILogger logger, string display);

    [LoggerMessage(EventId = 1406, Level = LogLevel.Debug, Message = "{ProcessName} stdout для дисплея {Display}: {Line}")]
    public static partial void LogVirtualDisplayProcessStdout(this ILogger logger, string processName, string display, string line);

    [LoggerMessage(EventId = 1407, Level = LogLevel.Warning, Message = "{ProcessName} stderr для дисплея {Display}: {Line}")]
    public static partial void LogVirtualDisplayProcessStderr(this ILogger logger, string processName, string display, string line);

    [LoggerMessage(EventId = 1408, Level = LogLevel.Warning, Message = "Не удалось завершить процессы виртуального дисплея {Display} за отведённое время. Остались PID: {RemainingProcessIds}")]
    public static partial void LogVirtualDisplayTerminationTimedOut(this ILogger logger, string display, string remainingProcessIds);
}