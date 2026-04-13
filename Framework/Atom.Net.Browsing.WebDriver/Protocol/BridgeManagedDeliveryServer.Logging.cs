using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class BridgeManagedDeliveryServerLogs
{
    [LoggerMessage(EventId = 1822, Level = LogLevel.Information, Message = "Managed-delivery HTTPS listener запущен на {Host}:{Port}")]
    public static partial void LogManagedDeliveryListenerStarted(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1823, Level = LogLevel.Information, Message = "Managed-delivery HTTPS listener остановлен на {Host}:{Port}")]
    public static partial void LogManagedDeliveryListenerStopped(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1824, Level = LogLevel.Debug, Message = "Managed-delivery payload настроен на порту {Port}: extensionId {ExtensionId}, updateUrl {UpdateUrl}, packageBytes {PackageBytesLength}")]
    public static partial void LogManagedDeliveryPayloadConfigured(this ILogger logger, int port, string extensionId, string updateUrl, int packageBytesLength);

    [LoggerMessage(EventId = 1825, Level = LogLevel.Debug, Message = "Managed-delivery payload очищен на порту {Port}")]
    public static partial void LogManagedDeliveryPayloadCleared(this ILogger logger, int port);

    [LoggerMessage(EventId = 1826, Level = LogLevel.Trace, Message = "Managed-delivery HTTPS listener получил запрос {Method} {Path} на порту {Port}")]
    public static partial void LogManagedDeliveryRequestReceived(this ILogger logger, int port, string method, string path);

    [LoggerMessage(EventId = 1827, Level = LogLevel.Debug, Message = "Managed-delivery HTTPS listener вернул {StatusCode} для {Method} {Path} на порту {Port}, причина {Reason}")]
    public static partial void LogManagedDeliveryRequestRejected(this ILogger logger, int port, string method, string path, int statusCode, string reason);

    [LoggerMessage(EventId = 1828, Level = LogLevel.Information, Message = "Managed-delivery HTTPS listener отдал manifest для расширения {ExtensionId} на порту {Port} по пути {Path}")]
    public static partial void LogManagedDeliveryManifestServed(this ILogger logger, int port, string extensionId, string path);

    [LoggerMessage(EventId = 1829, Level = LogLevel.Information, Message = "Managed-delivery HTTPS listener отдал CRX для расширения {ExtensionId} на порту {Port} по пути {Path}, байт {PackageBytesLength}")]
    public static partial void LogManagedDeliveryPackageServed(this ILogger logger, int port, string extensionId, string path, int packageBytesLength);

    [LoggerMessage(EventId = 1830, Level = LogLevel.Warning, Message = "Managed-delivery HTTPS listener не прошёл TLS согласование на порту {Port}")]
    public static partial void LogManagedDeliveryTlsHandshakeFailed(this ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 1831, Level = LogLevel.Debug, Message = "Managed-delivery HTTPS listener потерял клиентское соединение на порту {Port}")]
    public static partial void LogManagedDeliveryClientDisconnected(this ILogger logger, int port, Exception exception);
}