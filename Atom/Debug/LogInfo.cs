namespace Atom.Debug;

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

    public LogInfo(long id, string sourceMessage, string message, LogType type, DateTime time, object? data)
        => (Id, SourceMessage, Message, Type, Time, Data) = (id, sourceMessage, message, type, time, data);

    public LogInfo(string sourceMessage, string message, LogType type, DateTime time, object? data) : this(0, sourceMessage, message, type, time, data) { }

    public LogInfo(string sourceMessage, LogType type, DateTime time, object? data) : this(sourceMessage, string.Empty, type, time, data) { }

    public LogInfo(string sourceMessage, LogType type, object? data) : this(sourceMessage, type, DateTime.UtcNow, data) { }

    public LogInfo(LogType type, object? data) : this(string.Empty, type, data) { }
}

public record LogInfo<T> : LogInfo, ILogInfo<T>
{
    /// <inheritdoc/>
    public new T? Data { get; set; }

    /// <inheritdoc/>
    object? ILogInfo.Data => Data;

    /// <inheritdoc/>
    public LogInfo(long id, string sourceMessage, string message, LogType type, DateTime time, T? data) : base(id, sourceMessage, message, type, time, data) { }

    public LogInfo(string sourceMessage, string message, LogType type, DateTime time, T? data) : this(0, sourceMessage, message, type, time, data) { }

    public LogInfo(string sourceMessage, LogType type, DateTime time, T? data) : this(sourceMessage, string.Empty, type, time, data) { }

    public LogInfo(string sourceMessage, LogType type, T? data) : this(sourceMessage, type, DateTime.UtcNow, data) { }

    public LogInfo(LogType type, T? data) : this(string.Empty, type, data) { }
}