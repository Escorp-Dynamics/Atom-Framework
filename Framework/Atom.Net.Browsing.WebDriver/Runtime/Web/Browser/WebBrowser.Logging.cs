using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class WebBrowserLogs
{
    [LoggerMessage(EventId = 1810, Level = LogLevel.Information, Message = "Запуск браузера начинается, бинарный файл: {BinaryPath}, режим без окна: {Headless}, режим инкогнито: {Incognito}, внешний дисплей задан: {HasDisplay}")]
    public static partial void LogWebBrowserLaunchStarting(this ILogger logger, string binaryPath, bool headless, bool incognito, bool hasDisplay);

    [LoggerMessage(EventId = 1811, Level = LogLevel.Debug, Message = "Для браузера автоматически создаётся виртуальный дисплей")]
    public static partial void LogWebBrowserAutoDisplayCreating(this ILogger logger);

    [LoggerMessage(EventId = 1812, Level = LogLevel.Information, Message = "Для браузера создан виртуальный дисплей {Display}, видимый режим: {IsVisible}")]
    public static partial void LogWebBrowserAutoDisplayCreated(this ILogger logger, string display, bool isVisible);

    [LoggerMessage(EventId = 1813, Level = LogLevel.Debug, Message = "Материализация профиля браузера завершена, путь: {ProfilePath}")]
    public static partial void LogWebBrowserProfileMaterialized(this ILogger logger, string profilePath);

    [LoggerMessage(EventId = 1814, Level = LogLevel.Information, Message = "Запускается процесс браузера {BinaryPath}, дисплей: {Display}")]
    public static partial void LogWebBrowserProcessStarting(this ILogger logger, string binaryPath, string display);

    [LoggerMessage(EventId = 1815, Level = LogLevel.Information, Message = "Браузерный процесс {BinaryPath} успешно запущен")]
    public static partial void LogWebBrowserProcessStarted(this ILogger logger, string binaryPath);

    [LoggerMessage(EventId = 1816, Level = LogLevel.Debug, Message = "Для браузера создаётся виртуальная мышь, дисплей: {Display}")]
    public static partial void LogWebBrowserMouseResolving(this ILogger logger, string display);

    [LoggerMessage(EventId = 1817, Level = LogLevel.Debug, Message = "Для браузера создаётся виртуальная клавиатура, дисплей: {Display}")]
    public static partial void LogWebBrowserKeyboardResolving(this ILogger logger, string display);

    [LoggerMessage(EventId = 1818, Level = LogLevel.Information, Message = "Начато освобождение браузера")]
    public static partial void LogWebBrowserDisposeStarting(this ILogger logger);

    [LoggerMessage(EventId = 1819, Level = LogLevel.Debug, Message = "Автоматически созданный виртуальный дисплей {Display} освобождён")]
    public static partial void LogWebBrowserOwnedDisplayDisposed(this ILogger logger, string display);

    [LoggerMessage(EventId = 1820, Level = LogLevel.Debug, Message = "Автоматически созданные устройства ввода освобождены, мышь: {HasMouse}, клавиатура: {HasKeyboard}")]
    public static partial void LogWebBrowserOwnedInputDisposed(this ILogger logger, bool hasMouse, bool hasKeyboard);

    [LoggerMessage(EventId = 1821, Level = LogLevel.Debug, Message = "Материализованный профиль браузера очищен, путь: {ProfilePath}")]
    public static partial void LogWebBrowserProfileCleaned(this ILogger logger, string profilePath);

    [LoggerMessage(EventId = 1822, Level = LogLevel.Information, Message = "Освобождение браузера завершено")]
    public static partial void LogWebBrowserDisposeCompleted(this ILogger logger);

    [LoggerMessage(EventId = 1823, Level = LogLevel.Debug, Message = "Браузер принял событие жизненного цикла {EventType} для вкладки {TabId}, адрес: {Url}")]
    public static partial void LogWebBrowserLifecycleEventReceived(this ILogger logger, string eventType, string tabId, string url);

    [LoggerMessage(EventId = 1824, Level = LogLevel.Trace, Message = "Браузер начал поиск {Scope} по {QueryKind}: {Query}")]
    public static partial void LogWebBrowserLookupStarting(this ILogger logger, string scope, string queryKind, string query);

    [LoggerMessage(EventId = 1825, Level = LogLevel.Trace, Message = "Браузер завершил поиск {Scope} по {QueryKind}: {Query}, результат: {Result}")]
    public static partial void LogWebBrowserLookupCompleted(this ILogger logger, string scope, string queryKind, string query, string result);

    [LoggerMessage(EventId = 1826, Level = LogLevel.Trace, Message = "Браузер синхронизировал мостовое событие {EventType} для вкладки {TabId}")]
    public static partial void LogWebBrowserBridgeEventSynced(this ILogger logger, string eventType, string tabId);

    [LoggerMessage(EventId = 1827, Level = LogLevel.Debug, Message = "Браузер обрабатывает callback {CallbackName} для вкладки {TabId}, аргументов: {ArgumentCount}")]
    public static partial void LogWebBrowserCallbackDispatching(this ILogger logger, string callbackName, string tabId, int argumentCount);

    [LoggerMessage(EventId = 1828, Level = LogLevel.Warning, Message = "Браузер не нашёл вкладку для callback {CallbackName} и таба {TabId}")]
    public static partial void LogWebBrowserCallbackSkipped(this ILogger logger, string callbackName, string tabId);
}