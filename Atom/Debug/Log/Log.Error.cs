namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask ErrorAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Error, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => ErrorAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, LogMode mode, T? data) => ErrorAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, T? data, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, T? data) => ErrorAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync<T>(string message, ConsoleColor color, T? data) => ErrorAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask ErrorAsync(string message, LogMode mode, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, mode, color, ex?.ToDictionary(), cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode, ConsoleColor color, Exception ex) => ErrorAsync(message, mode, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode, Exception ex) => ErrorAsync(message, mode, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, Exception ex) => ErrorAsync(message, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, ConsoleColor color, Exception ex) => ErrorAsync(message, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask ErrorAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => ErrorAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode, ConsoleColor color) => ErrorAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, LogMode mode) => ErrorAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message) => ErrorAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ErrorAsync(string message, ConsoleColor color) => ErrorAsync(message, color, CancellationToken.None);
}