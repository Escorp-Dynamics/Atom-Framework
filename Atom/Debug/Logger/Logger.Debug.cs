namespace Atom.Debug;

public partial class Logger
{
    /// <inheritdoc/>
    public virtual ValueTask DebugAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        Add(message, mode, LogType.Debug, color, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => DebugAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, LogMode mode, T? data) => DebugAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, T? data) => DebugAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, ConsoleColor color, T? data) => DebugAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask DebugAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        AddToConsole(message, LogType.Debug, color, data);
        AddToFileFormatted(messageForFile, LogType.Debug, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => DebugAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => DebugAsync(message, messageForFile, ConsoleColor.Gray, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string? message, string messageForFile, T? data) => DebugAsync(message, messageForFile, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask DebugAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, LogMode mode, ConsoleColor color) => DebugAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, LogMode mode, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, LogMode mode) => DebugAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message) => DebugAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, ConsoleColor color) => DebugAsync(message, color, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask DebugAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync<object>(message, messageForFile, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, string messageForFile, ConsoleColor color) => DebugAsync(message, messageForFile, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, string messageForFile, CancellationToken cancellationToken) => DebugAsync(message, messageForFile, ConsoleColor.Gray, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string? message, string messageForFile) => DebugAsync(message, messageForFile, CancellationToken.None);
}