namespace Atom.Debug;

public partial class Logger
{
    /// <inheritdoc/>
    public virtual ValueTask WarningAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        Add(message, mode, LogType.Warning, color, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => WarningAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, LogMode mode, T? data) => WarningAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, T? data) => WarningAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public virtual ValueTask WarningAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        AddToConsole(message, LogType.Warning, color, data);
        AddToFileFormatted(messageForFile, LogType.Warning, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => WarningAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => WarningAsync(message, messageForFile, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync<T>(string? message, string messageForFile, T? data) => WarningAsync(message, messageForFile, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WarningAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, LogMode mode, ConsoleColor color) => WarningAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, LogMode mode, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, LogMode mode) => WarningAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message) => WarningAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, ConsoleColor color) => WarningAsync(message, color, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WarningAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync<object>(message, messageForFile, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, string messageForFile, ConsoleColor color) => WarningAsync(message, messageForFile, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, string messageForFile, CancellationToken cancellationToken) => WarningAsync(message, messageForFile, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WarningAsync(string? message, string messageForFile) => WarningAsync(message, messageForFile, CancellationToken.None);
}