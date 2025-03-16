namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет уровни логирования для протокола WebDriver Bidi.
/// </summary>
public enum BiDiLogLevel
{
    /// <summary>
    /// Уровень трассировки.
    /// </summary>
    Trace,
    /// <summary>
    /// Уровень отладки.
    /// </summary>
    Debug,
    /// <summary>
    /// Уровень информации.
    /// </summary>
    Info,
    /// <summary>
    /// Уровень предупреждения.
    /// </summary>
    Warn,
    /// <summary>
    /// Уровень ошибки.
    /// </summary>
    Error,
    /// <summary>
    /// Уровень фатальной ошибки.
    /// </summary>
    Fatal,
    /// <summary>
    /// Отключает все логи.
    /// </summary>
    Off,
}