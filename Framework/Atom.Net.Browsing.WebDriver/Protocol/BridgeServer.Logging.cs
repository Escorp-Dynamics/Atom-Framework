using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal static partial class BridgeServerLogs
{
    [LoggerMessage(EventId = 1800, Level = LogLevel.Information, Message = "Мостовой сервер запущен на {Host}:{Port}")]
    public static partial void LogBridgeServerStarted(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Debug, Message = "Мостовой сервер принял запрос проверки состояния {Path}")]
    public static partial void LogBridgeServerHealthRequested(this ILogger logger, string path);

    [LoggerMessage(EventId = 1802, Level = LogLevel.Warning, Message = "Мостовой сервер получил неподдерживаемый HTTP-запрос {Method} {Path}")]
    public static partial void LogBridgeServerHttpRequestUnsupported(this ILogger logger, string method, string path);

    [LoggerMessage(EventId = 1803, Level = LogLevel.Warning, Message = "Мостовой сервер получил WebSocket-запрос {Path}, но маршрутизация транспорта ещё не включена")]
    public static partial void LogBridgeServerWebSocketNotEnabled(this ILogger logger, string path);

    [LoggerMessage(EventId = 1804, Level = LogLevel.Information, Message = "Мостовой сервер остановлен на {Host}:{Port}")]
    public static partial void LogBridgeServerStopped(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1805, Level = LogLevel.Warning, Message = "Мостовой сервер завершил приём соединений из-за ошибки слушателя на {Host}:{Port}")]
    public static partial void LogBridgeServerAcceptFailed(this ILogger logger, Exception exception, string host, int port);

    [LoggerMessage(EventId = 1806, Level = LogLevel.Trace, Message = "Мостовой сервер начал обработку соединения {Method} {Path}")]
    public static partial void LogBridgeServerConnectionHandlingStarted(this ILogger logger, string method, string path);

    [LoggerMessage(EventId = 1807, Level = LogLevel.Information, Message = "Мостовой сервер принял согласование для сеанса {SessionId}")]
    public static partial void LogBridgeServerHandshakeAccepted(this ILogger logger, string sessionId);

    [LoggerMessage(EventId = 1808, Level = LogLevel.Warning, Message = "Мостовой сервер отклонил согласование {CorrelationId} с кодом {RejectCode}")]
    public static partial void LogBridgeServerHandshakeRejected(this ILogger logger, string correlationId, string rejectCode);

    [LoggerMessage(EventId = 1809, Level = LogLevel.Information, Message = "Мостовой сервер связал соединение с сеансом {SessionId}")]
    public static partial void LogBridgeServerSessionConnected(this ILogger logger, string sessionId);

    [LoggerMessage(EventId = 1810, Level = LogLevel.Information, Message = "Мостовой сервер снял связь с сеансом {SessionId}")]
    public static partial void LogBridgeServerSessionDisconnected(this ILogger logger, string sessionId);

    [LoggerMessage(EventId = 1811, Level = LogLevel.Debug, Message = "Мостовой сервер отправил запрос {RequestId} для сеанса {SessionId}, вкладки {TabId}, команда {Command}")]
    public static partial void LogBridgeServerRequestSent(this ILogger logger, string requestId, string sessionId, string tabId, string command);

    [LoggerMessage(EventId = 1812, Level = LogLevel.Debug, Message = "Мостовой сервер завершил запрос {RequestId} для сеанса {SessionId}, вкладки {TabId}, команда {Command}, статус {Status}, ошибка {Error}")]
    public static partial void LogBridgeServerRequestCompleted(this ILogger logger, string requestId, string sessionId, string tabId, string command, string status, string error);

    [LoggerMessage(EventId = 1813, Level = LogLevel.Warning, Message = "Мостовой сервер завершил запрос {RequestId} для сеанса {SessionId} и вкладки {TabId} с причиной {Reason}")]
    public static partial void LogBridgeServerRequestFailed(this ILogger logger, string requestId, string sessionId, string tabId, string reason);

    [LoggerMessage(EventId = 1814, Level = LogLevel.Warning, Message = "Мостовой сервер закрыл соединение сеанса {SessionId} из-за нарушения протокола {Reason}")]
    public static partial void LogBridgeServerProtocolViolation(this ILogger logger, string sessionId, string reason);

    [LoggerMessage(EventId = 1815, Level = LogLevel.Warning, Message = "Мостовой сервер отклонил ответ {MessageId} с причиной {Reason}")]
    public static partial void LogBridgeServerResponseRejected(this ILogger logger, string messageId, string reason);

    [LoggerMessage(EventId = 1816, Level = LogLevel.Information, Message = "Мостовой сервер зарегистрировал вкладку {TabId} окна {WindowId} для сеанса {SessionId}")]
    public static partial void LogBridgeServerTabRegistered(this ILogger logger, string sessionId, string tabId, string windowId);

    [LoggerMessage(EventId = 1817, Level = LogLevel.Information, Message = "Мостовой сервер снял вкладку {TabId} с сеанса {SessionId}")]
    public static partial void LogBridgeServerTabRemoved(this ILogger logger, string sessionId, string tabId);

    [LoggerMessage(EventId = 1818, Level = LogLevel.Information, Message = "Мостовой сервер завершил цикл приёма на {Host}:{Port}")]
    public static partial void LogBridgeServerAcceptLoopStopped(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1819, Level = LogLevel.Warning, Message = "Мостовой сервер завершил WebSocket-соединение {SessionId} из-за ошибки транспорта")]
    public static partial void LogBridgeServerWebSocketDisconnected(this ILogger logger, string sessionId, Exception exception);

    [LoggerMessage(EventId = 1820, Level = LogLevel.Information, Message = "Managed-delivery TLS trust подтверждён на порту {ManagedDeliveryPort}, метод {Method}, статус {Status}, detail {Detail}")]
    public static partial void LogBridgeServerManagedDeliveryTrustResolved(this ILogger logger, int managedDeliveryPort, string method, string status, string? detail);

    [LoggerMessage(EventId = 1821, Level = LogLevel.Warning, Message = "Managed-delivery TLS trust не подтверждён на порту {ManagedDeliveryPort}, метод {Method}, detail {Detail}, будут добавлены fallback-флаги браузера")]
    public static partial void LogBridgeServerManagedDeliveryTrustBypassRequired(this ILogger logger, int managedDeliveryPort, string method, string? detail);

    [LoggerMessage(EventId = 1822, Level = LogLevel.Information, Message = "Navigation proxy сервер запущен на {Host}:{Port}")]
    public static partial void LogBridgeServerNavigationProxyStarted(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1823, Level = LogLevel.Information, Message = "Navigation proxy сервер остановлен на {Host}:{Port}")]
    public static partial void LogBridgeServerNavigationProxyStopped(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1824, Level = LogLevel.Debug, Message = "Navigation proxy сопоставил решение {Action} для {Method} {Url}")]
    public static partial void LogBridgeServerNavigationProxyMatched(this ILogger logger, string action, string method, string url);

    [LoggerMessage(EventId = 1825, Level = LogLevel.Warning, Message = "Navigation proxy отклонил запрос {Method} {Target} по причине {Reason}")]
    public static partial void LogBridgeServerNavigationProxyRejected(this ILogger logger, string method, string target, string reason);

    [LoggerMessage(EventId = 1832, Level = LogLevel.Information, Message = "Bridge debug-event получен: kind {Kind}, sessionId {SessionId}, details {Details}")]
    public static partial void LogBridgeServerDebugEventReceived(this ILogger logger, string kind, string sessionId, string details);

    [LoggerMessage(EventId = 1833, Level = LogLevel.Warning, Message = "Bridge debug-event отклонён: причина {Reason}")]
    public static partial void LogBridgeServerDebugEventRejected(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1834, Level = LogLevel.Warning, Message = "Bridge debug-event содержит неверный JSON")]
    public static partial void LogBridgeServerDebugEventPayloadInvalid(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1835, Level = LogLevel.Debug, Message = "Мостовой сервер принял callback HTTP-запрос {RequestId} для вкладки {TabId}, callback {CallbackName}, аргументов: {ArgumentCount}")]
    public static partial void LogBridgeServerCallbackRequestReceived(this ILogger logger, string requestId, string tabId, string callbackName, int argumentCount);

    [LoggerMessage(EventId = 1836, Level = LogLevel.Debug, Message = "Мостовой сервер завершил callback HTTP-запрос {RequestId} для вкладки {TabId}, callback {CallbackName}, действие {Action}")]
    public static partial void LogBridgeServerCallbackRequestCompleted(this ILogger logger, string requestId, string tabId, string callbackName, string action);
}