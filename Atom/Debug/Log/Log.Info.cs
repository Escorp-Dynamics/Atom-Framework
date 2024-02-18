namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask InfoAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Info, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => InfoAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => InfoAsync(message, mode, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, LogMode mode, T? data) => InfoAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, T? data, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, T? data) => InfoAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync<T>(string message, ConsoleColor color, T? data) => InfoAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask InfoAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => InfoAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, LogMode mode, ConsoleColor color) => InfoAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, LogMode mode, CancellationToken cancellationToken) => InfoAsync(message, mode, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, LogMode mode) => InfoAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message) => InfoAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask InfoAsync(string message, ConsoleColor color) => InfoAsync(message, color, CancellationToken.None);
}