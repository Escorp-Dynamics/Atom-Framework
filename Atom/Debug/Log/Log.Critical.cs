namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Critical, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => CriticalAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, LogMode mode, T? data) => CriticalAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, T? data) => CriticalAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string message, ConsoleColor color, T? data) => CriticalAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, mode, color, ex?.ToDictionary(), cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, Exception ex) => CriticalAsync(message, mode, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode, Exception ex) => CriticalAsync(message, mode, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, Exception ex) => CriticalAsync(message, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, ConsoleColor color, Exception ex) => CriticalAsync(message, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color) => CriticalAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, LogMode mode) => CriticalAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message) => CriticalAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string message, ConsoleColor color) => CriticalAsync(message, color, CancellationToken.None);
}