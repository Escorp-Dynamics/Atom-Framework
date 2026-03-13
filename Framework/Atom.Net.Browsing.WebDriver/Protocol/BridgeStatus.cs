namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Статус ответа от расширения.
/// </summary>
public enum BridgeStatus
{
    /// <summary>
    /// Команда выполнена успешно.
    /// </summary>
    Ok,

    /// <summary>
    /// Ошибка при выполнении команды.
    /// </summary>
    Error,

    /// <summary>
    /// Элемент не найден.
    /// </summary>
    NotFound,

    /// <summary>
    /// Истёк таймаут ожидания.
    /// </summary>
    Timeout,

    /// <summary>
    /// Вкладка не подключена.
    /// </summary>
    Disconnected,
}
