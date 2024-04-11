namespace Atom.Debug;

/// <summary>
/// Представляет данные записи журнала событий.
/// </summary>
public record LogInfo : ILogInfo
{
    /// <inheritdoc/>
    public long Id { get; set; }

    /// <inheritdoc/>
    public string SourceMessage { get; set; }

    /// <inheritdoc/>
    public string Message { get; set; }

    /// <inheritdoc/>
    public LogType Type { get; set; }

    /// <inheritdoc/>
    public DateTime Time { get; set; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public object? Data { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo"/>.
    /// </summary>
    /// <param name="id">Уникальный идентификатор записи.</param>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="message">Сообщение после форматирования.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(long id, string sourceMessage, string message, LogType type, DateTime time, object? data)
        => (Id, SourceMessage, Message, Type, Time, Data) = (id, sourceMessage, message, type, time, data);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="message">Сообщение после форматирования.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, string message, LogType type, DateTime time, object? data) : this(0, sourceMessage, message, type, time, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, LogType type, DateTime time, object? data) : this(sourceMessage, string.Empty, type, time, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, LogType type, object? data) : this(sourceMessage, type, DateTime.UtcNow, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo"/>.
    /// </summary>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(LogType type, object? data) : this(string.Empty, type, data) { }
}

/// <summary>
/// Представляет данные записи журнала событий.
/// </summary>
/// <typeparam name="T">Тип связанных данных.</typeparam>
public record LogInfo<T> : LogInfo, ILogInfo<T>
{
    /// <inheritdoc/>
    public new T? Data { get; set; }

    /// <inheritdoc/>
    object? ILogInfo.Data => Data;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo{T}"/>.
    /// </summary>
    /// <param name="id">Уникальный идентификатор записи.</param>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="message">Сообщение после форматирования.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(long id, string sourceMessage, string message, LogType type, DateTime time, T? data) : base(id, sourceMessage, message, type, time, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo{T}"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="message">Сообщение после форматирования.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, string message, LogType type, DateTime time, T? data) : this(0, sourceMessage, message, type, time, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo{T}"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="time">Время создания сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, LogType type, DateTime time, T? data) : this(sourceMessage, string.Empty, type, time, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo{T}"/>.
    /// </summary>
    /// <param name="sourceMessage">Исходное сообщение с форматированием.</param>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(string sourceMessage, LogType type, T? data) : this(sourceMessage, type, DateTime.UtcNow, data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LogInfo{T}"/>.
    /// </summary>
    /// <param name="type">Тип записи журнала.</param>
    /// <param name="data">Связанные данные.</param>
    public LogInfo(LogType type, T? data) : this(string.Empty, type, data) { }
}