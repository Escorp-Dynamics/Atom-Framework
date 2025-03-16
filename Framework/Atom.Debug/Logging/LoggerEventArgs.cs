using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет аргументы события записи в журнал.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="LoggerEventArgs{TState}"/>.
/// </remarks>
/// <param name="scope">Контекст логирования.</param>
/// <param name="level">Уровень логирования.</param>
/// <param name="eventId">Идентификатор события логирования.</param>
/// <param name="state">Данные состояния контекста.</param>
/// <param name="exception">Связанное исключение.</param>
/// <param name="formatter">Функция форматирования.</param>
public class LoggerEventArgs<TState>(string? scope, LogLevel level, EventId eventId, TState? state, Exception? exception, Func<TState, Exception?, string>? formatter) : MutableEventArgs
{
    /// <summary>
    /// Контекст логирования.
    /// </summary>
    public string? Scope { get; protected internal set; } = scope;

    /// <summary>
    /// Уровень логирования.
    /// </summary>
    public LogLevel Level { get; set; } = level;

    /// <summary>
    /// Идентификатор события логирования.
    /// </summary>
    public EventId EventId { get; set; } = eventId;

    /// <summary>
    /// Данные состояния контекста.
    /// </summary>
    public TState? State { get; set; } = state;

    /// <summary>
    /// Связанное исключение.
    /// </summary>
    public Exception? Exception { get; set; } = exception;

    /// <summary>
    /// Функция форматирования.
    /// </summary>
    public Func<TState, Exception?, string>? Formatter { get; set; } = formatter;

    /// <summary>
    /// Время создания события.
    /// </summary>
    public DateTime DateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LoggerEventArgs{TState}"/>.
    /// </summary>
    public LoggerEventArgs() : this(default, default, default, default, default, default) { }
}