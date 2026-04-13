namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет тип навигационного действия страницы.
/// </summary>
public enum NavigationKind
{
    /// <summary>
    /// Выполняет обычный переход по адресу.
    /// </summary>
    Default,

    /// <summary>
    /// Выполняет переход назад по истории.
    /// </summary>
    Back,

    /// <summary>
    /// Выполняет переход вперёд по истории.
    /// </summary>
    Forward,

    /// <summary>
    /// Выполняет перезагрузку текущей страницы.
    /// </summary>
    Reload,
}