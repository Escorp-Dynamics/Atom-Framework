using Atom.Threading;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Atom.Debug;

public partial class Logger : ILogger
{
    private const string BackgroundMessage = "Фоновое ожидание";

    private readonly Log log;
    private readonly ConcurrentQueue<Tuple<LogMode, ILogInfo<object>>> queue;

    private string? currentInput;
    private bool isNoResetLastLine;

    private DateTime lastUpdateTime = DateTime.UtcNow;
    private DateTime beginWaitingTime = DateTime.UtcNow;
    private string? lastMessage;
    private readonly ObservableCollection<IConsoleCommand> commands = [];

    public string Name => log.Name;

    public LogMode Mode
    {
        get => log.Mode;
        set => log.Mode = value;
    }

    public string? Path => log.Path;

    public bool IsFormattingEnabled
    {
        get => log.IsFormattingEnabled;
        set => log.IsFormattingEnabled = value;
    }

    public IDictionary<LogMode, LogType> Filter { get; } = new Dictionary<LogMode, LogType>
    {
        { LogMode.Console, LogType.All },
        { LogMode.File, LogType.All },
        { LogMode.Database, LogType.All },
        { LogMode.All, LogType.All },
    };

    public bool IsEnabled { get; set; }

    /// <inheritdoc/>
    public virtual IEnumerable<IConsoleCommand> Commands => commands;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILog, LogEventArgs>? Writting;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILogger, InputEventArgs>? Command;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILogger, NotifyCollectionChangedEventArgs>? CommandsChanged;

    public Logger(string? name)
    {
        log = new(name ?? "log");
        queue = new ConcurrentQueue<Tuple<LogMode, ILogInfo<object>>>();
        commands.CollectionChanged += async (s, e) => await OnCommandsChanged(e).ConfigureAwait(false);
    }

    private void AddToConsole(string? message, LogType type, ConsoleColor color, object? data)
    {
        var info = new LogInfo<object>(GetMessage(message, color) ?? string.Empty, type, lastUpdateTime = DateTime.UtcNow, data);

        if (IsEnabled)
        {
            queue.Enqueue(new Tuple<LogMode, ILogInfo<object>>(LogMode.Console, info));
            return;
        }

        log.WriteLineAsync(info, LogMode.Console).AsTask();
    }

    private void AddToFile(string? message, LogType type, object? data)
    {
        var info = new LogInfo<object>(GetMessage(message, type), type, data);

        if (IsEnabled)
        {
            queue.Enqueue(new Tuple<LogMode, ILogInfo<object>>(LogMode.File, info));
            return;
        }

        log.WriteLineAsync(info, LogMode.File).AsTask();
    }

    private void AddToFileFormatted(string? message, LogType type, object? data)
    {
        var info = new LogInfo<object>(type, data) { Message = GetMessage(message, type), };

        if (IsEnabled)
        {
            queue.Enqueue(new Tuple<LogMode, ILogInfo<object>>(LogMode.File, info));
            return;
        }

        log.WriteLineAsync(info, LogMode.File).AsTask();
    }

    private void Add(string? message, LogMode mode, LogType type, ConsoleColor color = ConsoleColor.White, object? data = default, bool isHeader = false, string? prefix = default)
    {
        if (!string.IsNullOrEmpty(message))
        {
            if (lastMessage != message && type is not LogType.Service && lastMessage is not BackgroundMessage)
            {
                beginWaitingTime = DateTime.UtcNow;
                lastMessage = message;
            }

            if (message is BackgroundMessage)
            {
                var time = DateTime.UtcNow - beginWaitingTime;
                if (time.TotalSeconds >= 1) message += $"  [dy]{time:hh\\:mm\\:ss}[/dy]";
            }

            if (!isHeader)
            {
                prefix ??= !IsEnabled || type is LogType.Service ? string.Format("{0,12}", ' ') : DateTime.UtcNow.ToString("HH:mm:ss.fff");
                var pfx = string.Empty;

                if (!mode.HasFlag(LogMode.File) && !mode.HasFlag(LogMode.Database))
                {
                    if (!string.IsNullOrEmpty(currentInput))
                    {
                        pfx += $"[sr] {currentInput} [/sr]";
                        //if (filteredWorkers.Count is 0) pfx += ' ';   // TODO: Дописать форматирование под фильтр воркеров.
                    }

                    //if (filteredWorkers.Count is not 0)
                    //    pfx += $"[w:dm] [/w:dm] ";
                    //else if (string.IsNullOrEmpty(currentInput))
                    //    pfx += "  ";
                }

                if (mode.HasFlag(LogMode.Console)) pfx += "  ";
                message = $"{pfx}{prefix}  {message}";
            }
        }

        if (mode.HasFlag(LogMode.Console)) AddToConsole(message, type, color, data);

        if (mode.HasFlag(LogMode.File))
        {
            if (isHeader) message = message?.Trim();
            AddToFile(message, type, data);
        }
    }

    private async ValueTask ShowBackgroundMessageAsync()
    {
        if ((DateTime.UtcNow - lastUpdateTime).TotalSeconds < 1) return;

        var message = BackgroundMessage;

        if (message.Equals(lastMessage))
        {
            if (!isNoResetLastLine)
                await ResetLineAsync().ConfigureAwait(false);
            else
                isNoResetLastLine = default;
        }

        Add(message, LogMode.Console, LogType.Info, prefix: $"{beginWaitingTime:HH:mm:ss.fff}");
    }

    protected virtual ValueTask OnCommandsChanged(NotifyCollectionChangedEventArgs e) => CommandsChanged.On(this, e);

    protected virtual async void OnBackground()
    {
        if (!IsEnabled) return;

        if (!queue.TryDequeue(out var info))
        {
            await ShowBackgroundMessageAsync().ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            OnBackground();
            return;
        }

        await log.WriteLineAsync(info.Item2, info.Item1).ConfigureAwait(false);
        OnBackground();
    }

    protected virtual ValueTask OnWritting(ILog sender, LogEventArgs e) => Writting.On(sender, e);

    protected virtual async void OnCommand()
    {
        await Wait.UntilAsync(() => IsEnabled && !Console.KeyAvailable, TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

        if (!IsEnabled) return;

        var command = string.Empty;
        ConsoleKeyInfo keyInfo;

        do
        {
            keyInfo = Console.ReadKey(true);
            command += keyInfo.KeyChar;
            currentInput = command;
        } while (keyInfo.Key is not ConsoleKey.Enter);

        currentInput = default;
        command = command?.Trim();

        if (string.IsNullOrEmpty(command))
        {
            OnCommand();
            return;
        }

        var eventArgs = new InputEventArgs(command);
        await Command.On(this, eventArgs).ConfigureAwait(false);

        if (eventArgs.IsCancelled)
        {
            OnCommand();
            return;
        }

        foreach (var c in commands)
            if (c.TryParse(command, out var args, out var isCancellation))
                isNoResetLastLine = !isCancellation
                    ? await c.ExecuteAsync(args).ConfigureAwait(false)
                    : await c.CancelAsync(args).ConfigureAwait(false);

        OnCommand();
    }

    /// <inheritdoc/>
    protected virtual void OnStarting()
    {
        log.Writting += OnWritting;

        OnBackground();
        OnCommand();
    }

    /// <inheritdoc/>
    public virtual ILogger UseCommand<TCommand>(TCommand command) where TCommand : class, IConsoleCommand
    {
        if (TryGetCommand<TCommand>(out var m) && m is not null) commands.Remove(m);
        commands.Add(command);
        return this;
    }

    /// <inheritdoc/>
    public ILogger UseCommand<TCommand>() where TCommand : class, IConsoleCommand, new() => UseCommand(new TCommand() { Log = this, });

    /// <inheritdoc/>
    public bool HasCommand<TCommand>() where TCommand : class, IConsoleCommand => commands.Any(x => x is TCommand);

    /// <inheritdoc/>
    public bool TryGetCommand<TCommand>(out TCommand? command) where TCommand : class, IConsoleCommand => (command = commands.OfType<TCommand>().FirstOrDefault()) is not null;

    public virtual ValueTask HeaderAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask HeaderAsync() => HeaderAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual void Flush() => SpinWait.SpinUntil(() => IsEnabled && !queue.IsEmpty, TimeSpan.FromMilliseconds(50));

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        await log.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public virtual ValueTask ResetLineAsync(int offset, CancellationToken cancellationToken) => log.ResetLineAsync(offset, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync(int offset) => ResetLineAsync(offset, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync(CancellationToken cancellationToken) => ResetLineAsync(0, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);

    private static string? GetMessage(string? message, ConsoleColor color)
    {
        if (color is ConsoleColor.White) return message;
        var colorName = color.AsString();
        return $"[{colorName}]{message}[/{colorName}]";
    }

    private static string GetMessage(string? message, LogType type)
    {
        var prefix = type switch
        {
            LogType.Warning => "WARNING",
            LogType.Error => "ERROR",
            LogType.Critical => "CRITICAL",
            _ => string.Empty,
        };

        return string.Format("{0,8} {1}", prefix, message);
    }
}