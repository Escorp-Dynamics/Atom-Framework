namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask WarningAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Warning, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => WarningAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, LogMode mode, T? data) => WarningAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, T? data) => WarningAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string message, ConsoleColor color, T? data) => WarningAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WarningAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, LogMode mode, ConsoleColor color) => WarningAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, LogMode mode, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, LogMode mode) => WarningAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message) => WarningAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string message, ConsoleColor color) => WarningAsync(message, color, CancellationToken.None);
}