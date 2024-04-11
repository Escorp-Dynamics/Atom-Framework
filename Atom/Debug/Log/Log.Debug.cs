namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask DebugAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Debug, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => DebugAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, LogMode mode, T? data) => DebugAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, T? data) => DebugAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync<T>(string message, ConsoleColor color, T? data) => DebugAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask DebugAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, LogMode mode, ConsoleColor color) => DebugAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, LogMode mode, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, LogMode mode) => DebugAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message) => DebugAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DebugAsync(string message, ConsoleColor color) => DebugAsync(message, color, CancellationToken.None);
}