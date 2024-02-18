namespace Atom.Debug;

public partial class Logger
{
    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        Add(message, mode, LogType.Success, color, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => SuccessAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, LogMode mode, T? data) => SuccessAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, T? data) => SuccessAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        AddToConsole(message, LogType.Success, color, data);
        AddToFileFormatted(messageForFile, LogType.Success, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => SuccessAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => SuccessAsync(message, messageForFile, ConsoleColor.White, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string? message, string messageForFile, T? data) => SuccessAsync(message, messageForFile, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, LogMode mode, ConsoleColor color) => SuccessAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, LogMode mode, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, LogMode mode) => SuccessAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message) => SuccessAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, ConsoleColor color) => SuccessAsync(message, color, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync<object>(message, messageForFile, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, string messageForFile, ConsoleColor color) => SuccessAsync(message, messageForFile, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, string messageForFile, CancellationToken cancellationToken) => SuccessAsync(message, messageForFile, ConsoleColor.White, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string? message, string messageForFile) => SuccessAsync(message, messageForFile, CancellationToken.None);
}