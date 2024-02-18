namespace Atom.Debug;

public partial class Log
{
    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken)
    {
        var colorName = color.AsString();
        return WriteLineAsync($"[{colorName}]{message}[/{colorName}]", LogType.Success, mode, data, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => SuccessAsync(message, mode, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.Green, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, LogMode mode, T? data) => SuccessAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, T? data) => SuccessAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync<T>(string message, ConsoleColor color, T? data) => SuccessAsync(message, color, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask SuccessAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync<object>(message, mode, color, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, LogMode mode, ConsoleColor color) => SuccessAsync(message, mode, color, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, LogMode mode, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.Green, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, LogMode mode) => SuccessAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message) => SuccessAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SuccessAsync(string message, ConsoleColor color) => SuccessAsync(message, color, CancellationToken.None);
}