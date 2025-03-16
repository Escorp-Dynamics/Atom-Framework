namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет данные события, возникающего при получении сообщения журнала из соединения WebDriver Bidi.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="LogMessageEventArgs"/> с сообщением журнала, уровнем журнала и именем компонента, из которого сообщение записывается в журнал.
/// </remarks>
/// <param name="message">Сообщение, отправленное в журнал.</param>
/// <param name="level">Уровень журнала сообщения, отправленного в журнал.</param>
/// <param name="componentName">Имя компонента, из которого сообщение записывается в журнал.</param>
public class LogMessageEventArgs(string message, BiDiLogLevel level, string componentName) : BiDiEventArgs
{
    /// <summary>
    /// Текст сообщения, отправленного в журнал.
    /// </summary>
    public string Message { get; } = message;

    /// <summary>
    /// Уровень журнала сообщения, отправленного в журнал.
    /// </summary>
    public BiDiLogLevel Level { get; } = level;

    /// <summary>
    /// Имя компонента, из которого это сообщение журнала было отправлено.
    /// </summary>
    public string ComponentName { get; } = componentName;

    /// <summary>
    /// Дату и время (в формате UTC) создания этой записи журнала.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}