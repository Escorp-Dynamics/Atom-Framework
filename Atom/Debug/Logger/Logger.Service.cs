namespace Atom.Debug;

public partial class Logger
{
    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        Add(message, mode, LogType.Service, color, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => ServiceAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, LogMode mode, T? data) => ServiceAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, T? data) => ServiceAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        AddToConsole(message, LogType.Service, color, data);
        AddToFileFormatted(messageForFile, LogType.Service, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => ServiceAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => ServiceAsync(message, messageForFile, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync<T>(string? message, string messageForFile, T? data) => ServiceAsync(message, messageForFile, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, LogMode mode, ConsoleColor color) => ServiceAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, LogMode mode, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, LogMode mode) => ServiceAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message) => ServiceAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, ConsoleColor color) => ServiceAsync(message, color, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask ServiceAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync<object>(message, messageForFile, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, string messageForFile, ConsoleColor color) => ServiceAsync(message, messageForFile, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, string messageForFile, CancellationToken cancellationToken) => ServiceAsync(message, messageForFile, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ServiceAsync(string? message, string messageForFile) => ServiceAsync(message, messageForFile, CancellationToken.None);
}