using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class BridgeSecureTransportServerLogs
{
    [LoggerMessage(EventId = 1840, Level = LogLevel.Information, Message = "WSS transport listener запущен на {Host}:{Port}")]
    public static partial void LogSecureTransportListenerStarted(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1841, Level = LogLevel.Information, Message = "WSS transport listener остановлен на {Host}:{Port}")]
    public static partial void LogSecureTransportListenerStopped(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1842, Level = LogLevel.Warning, Message = "WSS transport listener завершил приём из-за ошибки на {Host}:{Port}")]
    public static partial void LogSecureTransportAcceptFailed(this ILogger logger, Exception exception, string host, int port);

    [LoggerMessage(EventId = 1843, Level = LogLevel.Warning, Message = "WSS transport отклонил upgrade-запрос {Method} {Path} со статусом {StatusCode} по причине {Reason}")]
    public static partial void LogSecureTransportRequestRejected(this ILogger logger, string method, string path, int statusCode, string reason);

    [LoggerMessage(EventId = 1844, Level = LogLevel.Warning, Message = "WSS transport TLS negotiation завершилась ошибкой на порту {Port}")]
    public static partial void LogSecureTransportTlsHandshakeFailed(this ILogger logger, int port, Exception exception);

    [LoggerMessage(EventId = 1846, Level = LogLevel.Warning, Message = "WSS transport TLS detail на порту {Port}: {Message}")]
    public static partial void LogSecureTransportTlsHandshakeDetail(this ILogger logger, int port, string message, Exception exception);

    [LoggerMessage(EventId = 1845, Level = LogLevel.Warning, Message = "WSS transport клиент отключился до завершения upgrade на порту {Port}")]
    public static partial void LogSecureTransportClientDisconnected(this ILogger logger, int port, Exception exception);
}