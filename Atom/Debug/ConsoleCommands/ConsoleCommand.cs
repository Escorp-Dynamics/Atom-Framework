namespace Atom.Debug;

/// <summary>
/// Представляет базовую реализацию консольной команды.
/// </summary>
/// <param name="aliases">Коллекция псевдонимов команды.</param>
public abstract class ConsoleCommand(ILogger log, IEnumerable<string> aliases) : IConsoleCommand
{
    /// <inheritdoc/>
    public ILogger Log { get; init; } = log;

    /// <inheritdoc/>
    public IEnumerable<string> Aliases { get; protected set; } = aliases;

    /// <inheritdoc/>
    public virtual bool IsCancellable { get; protected set; } = true;

    /// <inheritdoc/>
    public abstract string Name { get; protected set; }

    /// <inheritdoc/>
    public event AsyncEventHandler<IConsoleCommand, ParseCommandEventArgs>? Parsing;

    /// <inheritdoc/>
    public event AsyncEventHandler<IConsoleCommand, ExecuteCommandEventArgs>? Execution;

    /// <inheritdoc/>
    public event AsyncEventHandler<IConsoleCommand, ExecuteCommandEventArgs>? Cancelling;

    /// <summary>
    /// Представляет базовую реализацию консольной команды. 
    /// </summary>
    /// <param name="aliases">Коллекция псевдонимов команды.</param>
    protected ConsoleCommand(ILogger log, string alias) : this(log, [alias]) { }

    protected virtual ValueTask OnParsing(ParseCommandEventArgs e) => Parsing.On(this, e);

    protected virtual async ValueTask<bool> OnExecution(ExecuteCommandEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e, nameof(e));
        await Execution.On(this, e).ConfigureAwait(false);
        return !e.IsCancelled && e.IsSuccess;
    }

    protected virtual async ValueTask<bool> OnCancelling(ExecuteCommandEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e, nameof(e));
        await Cancelling.On(this, e).ConfigureAwait(false);
        return !e.IsCancelled && e.IsSuccess;
    }

    /// <inheritdoc/>
    public virtual bool TryParse(string command, out IEnumerable<string> args, out bool isCancellation)
    {
        args = [];
        isCancellation = default;
        var eventArgs = new ParseCommandEventArgs(command, args) { IsCancelled = true, };

        if (string.IsNullOrEmpty(command))
        {
            OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
            return !eventArgs.IsCancelled;
        }

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is 0 || string.IsNullOrEmpty(parts[0]))
        {
            OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
            return !eventArgs.IsCancelled;
        }

        if (parts[0][0] is '-')
        {
            if (!IsCancellable)
            {
                OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
                return !eventArgs.IsCancelled;
            }

            isCancellation = true;
            parts[0] = parts[0][1..];
        }

        if (!Aliases.Any(x => x.Equals(parts[0], StringComparison.OrdinalIgnoreCase)))
        {
            OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
            return !eventArgs.IsCancelled;
        }

        eventArgs.IsCancelled = default;
        eventArgs.IsParsed = true;
        eventArgs.IsValid = true;

        if (parts.Length is 1)
        {
            OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
            return !eventArgs.IsCancelled;
        }

        var parsedArgs = new List<string>();

        for (var i = 1; i < parts.Length; ++i)
        {
            var parsedArg = parts[i].Trim('"', '\'').Trim();
            if (string.IsNullOrEmpty(parsedArg)) continue;
            parsedArgs.Add(parsedArg);
        }

        if (parsedArgs.Count is not 0) args = [.. parsedArgs];

        OnParsing(eventArgs).AsTask().GetAwaiter().GetResult();
        return !eventArgs.IsCancelled;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var eventArgs = new ExecuteCommandEventArgs(args);
        return OnExecution(eventArgs);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteAsync(IEnumerable<string> args) => ExecuteAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteAsync(params string[] args) => ExecuteAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync([], cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> CancelAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var eventArgs = new ExecuteCommandEventArgs(args);
        return OnCancelling(eventArgs);
    }

    /// <inheritdoc/>
    public ValueTask<bool> CancelAsync(IEnumerable<string> args) => CancelAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> CancelAsync(params string[] args) => CancelAsync(args, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<bool> CancelAsync(CancellationToken cancellationToken) => CancelAsync([], cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> CancelAsync() => CancelAsync(CancellationToken.None);
}