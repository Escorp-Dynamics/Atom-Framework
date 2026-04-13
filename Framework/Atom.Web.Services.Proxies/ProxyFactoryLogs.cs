using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies;

// Диапазон 1000-1099 зарезервирован под operational diagnostics ProxyFactory.
internal static partial class ProxyFactoryLogs
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Фабрика прокси выдала один прокси через путь адресного холодного старта.")]
    public static partial void SingleTargetedColdStartSatisfied(this ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Фабрика прокси выдала пакет из {Count} прокси через путь адресного холодного старта.")]
    public static partial void BatchTargetedColdStartSatisfied(this ILogger logger, int count);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Фабрика прокси вручную очистила {Count} блокировок прокси по явному списку.")]
    public static partial void ExplicitLeaseCleanup(this ILogger logger, int count);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Фабрика прокси вручную очистила {Count} блокировок прокси из полного набора.")]
    public static partial void FullLeaseCleanup(this ILogger logger, int count);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Фабрика прокси подключила провайдера {Provider}. Подключено провайдеров: {Count}.")]
    public static partial void ProviderAttached(this ILogger logger, string provider, int count);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Debug, Message = "Фабрика прокси обновила снимок провайдера {Provider}. Получено {Count} прокси.")]
    public static partial void ProviderSnapshotRefreshed(this ILogger logger, string provider, int count);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Фабрика прокси не смогла обновить снимок провайдера {Provider}. Последний успешный снимок на {Count} прокси сохранён.")]
    public static partial void ProviderSnapshotRefreshFailedPreserved(this ILogger logger, Exception exception, string provider, int count);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "Фабрика прокси не смогла обновить снимок провайдера {Provider}. Снимок будет очищен, потому что PreservePoolOnRefreshFailure отключён.")]
    public static partial void ProviderSnapshotRefreshFailedCleared(this ILogger logger, Exception exception, string provider);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Фабрика прокси отключила провайдера {Provider}. Подключено провайдеров: {Count}.")]
    public static partial void ProviderDetached(this ILogger logger, string provider, int count);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Фабрика прокси завершила перестроение. Активных прокси: {ProxyCount}. Подключено провайдеров: {ProviderCount}. Блокировок прокси: {BlockedCount}.")]
    public static partial void RebuildCompleted(this ILogger logger, int proxyCount, int providerCount, int blockedCount);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Debug, Message = "Фабрика прокси сняла по таймауту {Count} блокировок прокси.")]
    public static partial void ExpiredBlockedLeases(this ILogger logger, int count);
}