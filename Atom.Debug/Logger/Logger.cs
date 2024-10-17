using Atom.Architect.Reactive;
using Atom.Threading;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Timer = System.Timers.Timer;

namespace Atom.Debug;

/// <summary>
/// Представляет реализацию менеджера журнала событий.
/// </summary>
public partial class Logger : ILogger
{
    private const string BackgroundMessage = "Фоновое ожидание";

    private readonly Log log;
    private readonly ObservableCollection<IConsoleCommand> commands = [];
    private readonly ConcurrentQueue<Tuple<LogMode, ILogInfo<object>>> queue;
    private readonly Timer backgroundTimer = new(TimeSpan.FromMilliseconds(50));

    private string? currentInput;
    private bool isNoResetLastLine;

    private DateTime lastUpdateTime = DateTime.UtcNow;
    private DateTime beginWaitingTime = DateTime.UtcNow;
    private string? lastMessage;

    private event AsyncEventHandler<ILogger> CommandInternal;

    /// <inheritdoc/>
    public string Name => log.Name;

    /// <inheritdoc/>
    public LogMode Mode
    {
        get => log.Mode;
        set => log.Mode = value;
    }

    /// <inheritdoc/>
    public string? Path => log.Path;

    /// <inheritdoc/>
    public bool IsFormattingEnabled
    {
        get => log.IsFormattingEnabled;
        set => log.IsFormattingEnabled = value;
    }

    /// <inheritdoc/>
    public IDictionary<LogMode, LogType> Filter { get; } = new Dictionary<LogMode, LogType>
    {
        { LogMode.Console, LogType.All },
        { LogMode.File, LogType.All },
        { LogMode.Database, LogType.All },
        { LogMode.All, LogType.All },
    };

    /// <inheritdoc/>
    public bool IsBackground { get; set; }

    /// <inheritdoc/>
    public bool IsShowBackgroundMessage { get; set; }

    /// <inheritdoc/>
    public virtual IEnumerable<IConsoleCommand> Commands => commands;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILog, LogEventArgs>? Writing;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILogger, InputEventArgs>? Command;

    /// <inheritdoc/>
    public event AsyncEventHandler<ILogger, NotifyCollectionChangedEventArgs>? CommandsChanged;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Logger"/>
    /// </summary>
    /// <param name="name">Имя журнала.</param>
    public Logger(string? name)
    {
        queue = new ConcurrentQueue<Tuple<LogMode, ILogInfo<object>>>();
        commands.CollectionChanged += async (_, e) => await OnCommandsChanged(e).ConfigureAwait(false);

        backgroundTimer.AutoReset = true;
        backgroundTimer.Elapsed += async (_, _) => await OnBackground().ConfigureAwait(false);

        CommandInternal += OnCommandInternal;

        log = new Log(name ?? "log");
        log.Writing += OnWriting;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Logger"/>
    /// </summary>
    public Logger() : this(default) { }

    private void AddToConsole(string? message, LogType type, ConsoleColor color, object? data)
    {
        var info = new LogInfo<object>(GetMessage(message, color) ?? string.Empty, type, lastUpdateTime = DateTime.UtcNow, data);

        if (IsBackground)
        {
            queue.Enqueue(new Tuple<LogMode, ILogInfo<object>>(LogMode.Console, info));
            return;
        }

        log.WriteLineAsync(info, LogMode.Console).AsTask();
    }

    private void AddToFile(string? message, LogType type, object? data)
    {
        var info = new LogInfo<object>(GetMessage(message, type), type, data);

        if (IsBackground)
        {
            queue.Enqueue(new Tuple<LogMode, ILogInfo<object>>(LogMode.File, info));
            return;
        }

        log.WriteLineAsync(info, LogMode.File).AsTask();
    }

    private void AddToFileFormatted(string? message, LogType type, object? data)
    {
        var info = new LogInfo<object>(type, data) { Message = GetMessage(message, type), };

        if (IsBackground)
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
                if (time.TotalSeconds >= 1) message += $@"  [dy]{time:hh\:mm\:ss}[/dy]";
            }

            if (!isHeader)
            {
                prefix ??= !IsBackground || type is LogType.Service ? $"{' ',12}" : DateTime.UtcNow.ToString("HH:mm:ss.fff");
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
        if (!mode.HasFlag(LogMode.File)) return;
        if (isHeader) message = message?.Trim();

        AddToFile(message, type, data);
    }

    private async ValueTask ShowBackgroundMessageAsync()
    {
        if ((DateTime.UtcNow - lastUpdateTime).TotalSeconds < 1) return;

        if (BackgroundMessage.Equals(lastMessage))
        {
            if (!isNoResetLastLine)
                await ResetLineAsync().ConfigureAwait(false);
            else
                isNoResetLastLine = default;
        }

        Add(BackgroundMessage, LogMode.Console, LogType.Info, prefix: $"{beginWaitingTime:HH:mm:ss.fff}");
    }

    private async ValueTask OnCommandInternal(ILogger sender)
    {
        await Wait.UntilAsync(() => IsBackground && !Console.KeyAvailable, TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);

        var command = string.Empty;
        ConsoleKeyInfo keyInfo;

        do
        {
            if (!IsBackground) return;
            keyInfo = Console.ReadKey(true);
            command += keyInfo.KeyChar;
            currentInput = command;
        } while (keyInfo.Key is not ConsoleKey.Enter);

        currentInput = default;
        command = command.Trim();

        if (string.IsNullOrEmpty(command))
        {
            if (CommandInternal is not null) await CommandInternal(sender).ConfigureAwait(false);
            return;
        }

        var eventArgs = new InputEventArgs(command);
        if (Command is not null) await Command(this, eventArgs).ConfigureAwait(false);

        if (eventArgs.IsCancelled)
        {
            if (CommandInternal is not null) await CommandInternal(sender).ConfigureAwait(false);
            return;
        }

        foreach (var c in commands)
        {
            if (c.TryParse(command, out var args, out var isCancellation))
            {
                isNoResetLastLine = !isCancellation
                    ? await c.ExecuteAsync(args).ConfigureAwait(false)
                    : await c.CancelAsync(args).ConfigureAwait(false);
            }
        }

        if (CommandInternal is not null) await CommandInternal(sender).ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент изменения коллекции связанных команд.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    protected virtual ValueTask OnCommandsChanged(NotifyCollectionChangedEventArgs e) => CommandsChanged?.Invoke(this, e) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит в момент фоновой обработки журнала.
    /// </summary>
    protected virtual async ValueTask OnBackground()
    {
        if (!IsBackground) return;
        while (queue.TryDequeue(out var info)) await log.WriteLineAsync(info.Item2, info.Item1).ConfigureAwait(false);
        if (IsShowBackgroundMessage) await ShowBackgroundMessageAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент записи в журнал.
    /// </summary>
    /// <param name="sender">Экземпляр журнала.</param>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    protected virtual ValueTask OnWriting(ILog sender, LogEventArgs e) => Writing?.Invoke(sender, e) ?? ValueTask.CompletedTask;

    /// <summary>
    /// Происходит при запуске.
    /// </summary>
    protected virtual ValueTask OnStarting() => CommandInternal?.Invoke(this) ?? ValueTask.CompletedTask;

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

    /// <inheritdoc/>
    public virtual ValueTask HeaderAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask HeaderAsync() => HeaderAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual void Flush() => SpinWait.SpinUntil(() => IsBackground && !queue.IsEmpty, TimeSpan.FromMilliseconds(50));

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        IsBackground = false;

        backgroundTimer.Stop();
        backgroundTimer.Dispose();

        await log.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public virtual ValueTask ResetLineAsync(CancellationToken cancellationToken) => log.ResetLineAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken cancellationToken) => await OnStarting().ConfigureAwait(false);

    /// <inheritdoc/>
    public ValueTask StartAsync() => StartAsync(CancellationToken.None);

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

        return $"{prefix,8} {message}";
    }
}