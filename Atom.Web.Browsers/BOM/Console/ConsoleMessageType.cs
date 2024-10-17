namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Тип сообщения консоли.
/// </summary>
public enum ConsoleMessageType
{
    /// <summary>
    /// Журнал.
    /// </summary>
    Log,
    /// <summary>
    /// Трассировка.
    /// </summary>
    Trace,
    /// <summary>
    /// Отладка.
    /// </summary>
    Debug,
    /// <summary>
    /// Информация.
    /// </summary>
    Info,
    /// <summary>
    /// Предупреждение.
    /// </summary>
    Warn,
    /// <summary>
    /// Ошибка.
    /// </summary>
    Error,
}