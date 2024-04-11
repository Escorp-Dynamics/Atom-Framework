namespace Atom.Debug;

/// <summary>
/// Тип записи журнала.
/// </summary>
[Flags]
public enum LogType
{
    /// <summary>
    /// Не задан.
    /// </summary>
    None = 0,
    /// <summary>
    /// Служебная информация.
    /// </summary>
    Service = 1,
    /// <summary>
    /// Отладочная информация.
    /// </summary>
    Debug = 2,
    /// <summary>
    /// Информация.
    /// </summary>
    Info = 4,
    /// <summary>
    /// Предупреждение.
    /// </summary>
    Warning = 8,
    /// <summary>
    /// Ошибка.
    /// </summary>
    Error = 16,
    /// <summary>
    /// Критическая информация.
    /// </summary>
    Critical = 32,
    /// <summary>
    /// Информация об успешной процедуре.
    /// </summary>
    Success = 64,
    /// <summary>
    /// Любая информация.
    /// </summary>
    All = Service | Debug | Info | Warning | Error | Critical | Success,
}