namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет уровень сообщения, полученного из консоли браузерной страницы.
/// </summary>
public enum ConsoleMessageLevel
{
    /// <summary>
    /// Обычное информационное сообщение.
    /// </summary>
    Log,

    /// <summary>
    /// Расширенное информационное сообщение.
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

    /// <summary>
    /// Отладочное сообщение.
    /// </summary>
    Debug,
}