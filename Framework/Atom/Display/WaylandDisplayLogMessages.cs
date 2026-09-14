using Microsoft.Extensions.Logging;

namespace Atom.Display;

/// <summary>
/// Диагностика сеанса дисплея на композиторе Wayland.
/// </summary>
internal static partial class WaylandDisplayLogMessages
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Дисплей Wayland запущен: {Display}, разрешение {Width}x{Height}, вывод картинки: {IsVisible}")]
    public static partial void LogWaylandDisplayStarted(this ILogger logger, string display, int width, int height, bool isVisible);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Дисплей Wayland остановлен: {Display}")]
    public static partial void LogWaylandDisplayStopped(this ILogger logger, string display);
}
