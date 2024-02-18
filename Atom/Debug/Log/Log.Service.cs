namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Debug, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => ServiceAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.Gray, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, LogMode mode, T? data) => ServiceAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, T? data) => ServiceAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string message, ConsoleColor color, T? data) => ServiceAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, LogMode mode, ConsoleColor color) => ServiceAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, LogMode mode, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.Gray, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, LogMode mode) => ServiceAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message) => ServiceAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string message, ConsoleColor color) => ServiceAsync(message, color, CancellationToken.None);
}