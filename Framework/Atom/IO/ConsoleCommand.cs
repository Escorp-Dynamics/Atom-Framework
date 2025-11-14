using System.Runtime.CompilerServices;

namespace Atom.Debug;

/// <summary>
/// Представляет базовую реализацию консольной команды.
/// </summary>
/// <param name="aliases">Коллекция псевдонимов команды.</param>
public abstract class ConsoleCommand(IEnumerable<string> aliases) : IConsoleCommand
{
    /// <inheritdoc/>
    public IEnumerable<string> Aliases { get; protected set; } = aliases;

    /// <inheritdoc/>
    public virtual bool IsCancellable { get; protected set; } = true;

    /// <inheritdoc/>
    public abstract string Name { get; protected set; }

    /// <inheritdoc/>
    public event AsyncEventHandler<object, ParseCommandEventArgs>? Parsing;

    /// <inheritdoc/>
    public event AsyncEventHandler<object, ExecuteCommandEventArgs>? Execution;

    /// <inheritdoc/>
    public event AsyncEventHandler<object, ExecuteCommandEventArgs>? Cancelling;

    /// <summary>
    /// Представляет базовую реализацию консольной команды.
    /// </summary>
    /// <param name="alias">Псевдоним команды.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ConsoleCommand(string alias) : this([alias]) { }

    /// <summary>
    /// Происходит в момент парсинга команды.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual ValueTask OnParsingAsync(ParseCommandEventArgs e) => Parsing?.Invoke(this, e) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит в момент выполнения команды.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual async ValueTask<bool> OnExecutionAsync(ExecuteCommandEventArgs e)
    {
        if (Execution is not null) await Execution(this, e).ConfigureAwait(false);
        return e is { IsCancelled: false, IsSuccess: true };
    }

    /// <summary>
    /// Происходит в момент отмены команды.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual async ValueTask<bool> OnCancellingAsync(ExecuteCommandEventArgs e)
    {
        if (Cancelling is not null) await Cancelling(this, e).ConfigureAwait(false);
        return e is { IsCancelled: false, IsSuccess: true };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async ValueTask<ConsoleCommandParseResult> TryParseAsync(string command, CancellationToken cancellationToken = default)
    {
        var (isParsed, args, isCancellation) = ParseCommand(command);
        var eventArgs = CreateParseEventArgs(command, args, isParsed, cancellationToken);

        await OnParsingAsync(eventArgs).ConfigureAwait(false);

        var isSuccess = !eventArgs.IsCancelled;
        return new ConsoleCommandParseResult(isSuccess, args, isSuccess && isCancellation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool IsParsed, string[] Args, bool IsCancellation) ParseCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (false, Array.Empty<string>(), false);

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0])) return (false, Array.Empty<string>(), false);

        var (alias, isCancellation) = NormalizeAlias(parts[0]);
        if (string.IsNullOrEmpty(alias)) return (false, Array.Empty<string>(), false);
        if (!Aliases.Any(x => x.Equals(alias, StringComparison.OrdinalIgnoreCase))) return (false, Array.Empty<string>(), false);

        var args = ExtractArguments(parts);
        return (true, args, isCancellation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (string Alias, bool IsCancellation) NormalizeAlias(string rawAlias)
    {
        if (string.IsNullOrEmpty(rawAlias)) return (string.Empty, false);
        if (rawAlias[0] is not '-') return (rawAlias, false);

        if (!IsCancellable) return (string.Empty, false);
        return (rawAlias[1..], true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string[] ExtractArguments(string[] parts)
    {
        if (parts.Length <= 1) return [];

        var parsedArgs = new List<string>(parts.Length - 1);
        for (var i = 1; i < parts.Length; ++i)
        {
            var parsedArg = parts[i].Trim('"', '\'').Trim();
            if (string.IsNullOrEmpty(parsedArg)) continue;
            parsedArgs.Add(parsedArg);
        }

        return parsedArgs.Count == 0 ? [] : [.. parsedArgs];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ParseCommandEventArgs CreateParseEventArgs(string command, string[] args, bool isParsed, CancellationToken cancellationToken)
        => new(command, args)
        {
            CancellationToken = cancellationToken,
            IsCancelled = !isParsed,
            IsParsed = isParsed,
            IsValid = isParsed,
        };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> ExecuteAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var eventArgs = new ExecuteCommandEventArgs(args);
        return OnExecutionAsync(eventArgs);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> ExecuteAsync(params IEnumerable<string> args) => ExecuteAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync([], cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> CancelAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var eventArgs = new ExecuteCommandEventArgs(args);
        return OnCancellingAsync(eventArgs);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> CancelAsync(params IEnumerable<string> args) => CancelAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> CancelAsync(CancellationToken cancellationToken) => CancelAsync([], cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> CancelAsync() => CancelAsync(CancellationToken.None);
}
