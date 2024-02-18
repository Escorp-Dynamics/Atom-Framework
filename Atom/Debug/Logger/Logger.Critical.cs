namespace Atom.Debug;

public partial class Logger
{
    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        Add(message, mode, LogType.Critical, color, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => CriticalAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Red, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, LogMode mode, T? data) => CriticalAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, T? data) => CriticalAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, ConsoleColor color, T? data) => CriticalAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        AddToConsole(message, LogType.Critical, color, data);
        AddToFileFormatted(messageForFile, LogType.Critical, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => CriticalAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => CriticalAsync(message, messageForFile, ConsoleColor.Red, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync<T>(string? message, string messageForFile, T? data) => CriticalAsync(message, messageForFile, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string? message, LogMode mode, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, mode, color, ex?.ToDictionary(), cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode, ConsoleColor color, Exception ex) => CriticalAsync(message, mode, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Red, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode, Exception ex) => CriticalAsync(message, mode, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, Exception ex) => CriticalAsync(message, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, ConsoleColor color, Exception ex) => CriticalAsync(message, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string? message, string messageForFile, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, messageForFile, color, ex?.ToDictionary(), cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile, ConsoleColor color, Exception ex) => CriticalAsync(message, messageForFile, color, ex, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, messageForFile, ConsoleColor.Red, ex, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile, Exception ex) => CriticalAsync(message, messageForFile, ex, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode, ConsoleColor color) => CriticalAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Red, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, LogMode mode) => CriticalAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message) => CriticalAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, ConsoleColor color) => CriticalAsync(message, color, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask CriticalAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync<object>(message, messageForFile, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile, ConsoleColor color) => CriticalAsync(message, messageForFile, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile, CancellationToken cancellationToken) => CriticalAsync(message, messageForFile, ConsoleColor.Red, cancellationToken);

    /// <inheritdoc/>
    public ValueTask CriticalAsync(string? message, string messageForFile) => CriticalAsync(message, messageForFile, CancellationToken.None);
}