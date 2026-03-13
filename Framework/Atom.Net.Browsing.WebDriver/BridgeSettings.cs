namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Настройки моста между драйвером и расширением браузера.
/// </summary>
public sealed class BridgeSettings
{
    /// <summary>
    /// Хост, на котором запускается WebSocket-сервер.
    /// </summary>
    /// <remarks>
    /// По умолчанию — <c>127.0.0.1</c> для ограничения доступа только с локальной машины.
    /// </remarks>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// Порт WebSocket-сервера. <c>0</c> — автоматический выбор свободного порта.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Секретный токен для аутентификации расширения при подключении.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// Таймаут ожидания ответа от расширения на запрос.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Интервал отправки Ping-сообщений для поддержания связи.
    /// </summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Максимальный размер одного WebSocket-сообщения в байтах.
    /// </summary>
    public int MaxMessageSize { get; init; } = 16 * 1024 * 1024;
}
