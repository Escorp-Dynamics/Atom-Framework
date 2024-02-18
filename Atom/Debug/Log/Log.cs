using System.Text;

namespace Atom.Debug;

/// <summary>
/// Представляет журнал истории событий.
/// </summary>
public partial class Log : ILog
{
    private protected Stream? consoleStream;
    private FileStream? fileStream;

    private readonly SemaphoreSlim locker = new(1, 1);

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public LogMode Mode { get; set; }

    /// <inheritdoc/>
    public string? Path { get; protected set; }

    /// <inheritdoc/>
    public bool IsFormattingEnabled { get ; set; }

    /// <inheritdoc/>
    public event AsyncEventHandler<ILog, LogEventArgs>? Writting;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Log"/>.
    /// </summary>
    /// <param name="name">Имя журнала.</param>
    /// <param name="mode">Режим записи.</param>
    public Log(string name, LogMode mode)
    {
        Name = name;
        Mode = mode;

        if (mode.HasFlag(LogMode.File)) Path = $"{name}_{DateTime.UtcNow:dd-MM-yy_hh-mm-ss}.log";
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Log"/>.
    /// </summary>
    /// <param name="name">Имя журнала.</param>
    public Log(string name) : this(name, LogMode.All) { }

    /// <summary>
    /// Происходит в момент записи сообщеня в журнал.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    /// <returns></returns>
    protected virtual ValueTask OnWriting(LogEventArgs e) => Writting.On(this, e);

    /// <summary>
    /// Асинхронно записывает все буферы и высвобождает ресурсы.
    /// </summary>
    /// <returns></returns>
    public async ValueTask DisposeAsync()
    {
        if (consoleStream is not null) await consoleStream.DisposeAsync().ConfigureAwait(false);
        if (fileStream is not null) await fileStream.DisposeAsync().ConfigureAwait(false);
        locker.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Обычная запись

    /// <inheritdoc/>
    public virtual async ValueTask WriteAsync<T>(ILogInfo<T> info, LogMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info, nameof(info));

        if (Mode is LogMode.None) return;

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        var message = new StringBuilder();

        if (string.IsNullOrEmpty(info.SourceMessage))
        {
            var outText = new StringBuilder();

            var foregrounds = new Stack<ConsoleColor>([Console.ForegroundColor]);
            var backgrounds = new Stack<ConsoleColor>([Console.BackgroundColor]);

            var parsedData = string.Empty;
            var isParsing = false;
            var isClosing = false;

            for (var i = 0; i < info.Message.Length; ++i)
            {
                if (info.Message[i] is '[')
                {
                    if (isParsing)
                    {
                        message = message.Append('[' + parsedData);
                        parsedData = string.Empty;
                        if (Mode.HasFlag(LogMode.Console)) outText = outText.Append('[');
                    }

                    isParsing = true;
                    continue;
                }

                if (info.Message[i] is ']')
                {
                    if (isParsing)
                    {
                        var tmp = parsedData.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (tmp.Length > 0)
                        {
                            if (tmp[0].TryGetColor(out var color) && color.HasValue)
                            {
                                if (isClosing)
                                {
                                    if (color == foregrounds.Peek())
                                    {
                                        foregrounds.Pop();
                                        if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(foregrounds.Peek().AsString());
                                    }
                                    else
                                    {
                                        var output = $"[/{parsedData}]";
                                        message = message.Append(output);
                                        if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(output);
                                    }
                                }
                                else
                                {
                                    foregrounds.Push(color!.Value);
                                    if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(color.Value.AsString());
                                }

                                if (tmp.Length is 2 && tmp[1].TryGetColor(out color))
                                {
                                    if (isClosing)
                                    {
                                        if (color == backgrounds.Peek())
                                        {
                                            backgrounds.Pop();
                                            if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(backgrounds.Peek().AsString(true));
                                        }
                                        else
                                        {
                                            var output = $"[/{parsedData}]";
                                            message = message.Append(output);
                                            if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(output);
                                        }
                                    }
                                    else
                                    {
                                        backgrounds.Push(color!.Value);
                                        if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(color.Value.AsString(true));
                                    }
                                }
                            }
                            else if (tmp[0].TryGetStyle(out var style) && style.HasValue)
                            {
                                if (isClosing)
                                {
                                    if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(style.Value.AsString(true));
                                }
                                else
                                {
                                    if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(style.Value.AsString());
                                }
                            }
                            else
                            {
                                var output = $"[{(isClosing ? '/' : null)}{parsedData}]";
                                message = message.Append(output);
                                if (Mode.HasFlag(LogMode.Console)) outText = outText.Append(output);
                            }
                        }

                        isParsing = false;
                        isClosing = false;
                        parsedData = string.Empty;
                        continue;
                    }
                }

                if (isParsing)
                {
                    if (info.Message[i] is '/' && info.Message[i - 1] is '[')
                    {
                        isClosing = true;
                        continue;
                    }

                    parsedData += info.Message[i];
                    continue;
                }

                message = message.Append(info.Message[i]);
                outText = outText.Append(info.Message[i]);
            }

            info.Message = message.ToString();
            var eventArgs = new LogEventArgs(info);
            if (!string.IsNullOrEmpty(info.Message)) await OnWriting(eventArgs).ConfigureAwait(false);
            if (eventArgs.IsCancelled) return;

            if (Mode.HasFlag(LogMode.Console) && mode.HasFlag(LogMode.Console))
            {
                consoleStream ??= Console.OpenStandardOutput();
                var outputText = Console.IsOutputRedirected ? outText.ToString() : $"\x1b[0m{outText}\x1b[0m";
                var buffer = Encoding.UTF8.GetBytes(outputText);

                await consoleStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                await consoleStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (Mode.HasFlag(LogMode.File) && mode.HasFlag(LogMode.File) && !string.IsNullOrEmpty(Path) && !string.IsNullOrEmpty(info.Message))
        {
            fileStream ??= File.Open(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await fileStream.WriteAsync(Encoding.UTF8.GetBytes(info.Message), cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        locker.Release();
    }

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(ILogInfo<T> info, LogMode mode) => WriteAsync(info, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(ILogInfo<T> info, CancellationToken cancellationToken) => WriteAsync(info, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(ILogInfo<T> info) => WriteAsync(info, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WriteAsync<T>(string message, LogType type, LogMode mode, T? data, CancellationToken cancellationToken)
        => WriteAsync(new LogInfo<T>(default, message, message, type, DateTime.UtcNow, data), mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, LogType type, LogMode mode, T? data) => WriteAsync(message, type, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, LogType type, T? data, CancellationToken cancellationToken) => WriteAsync(message, type, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, LogType type, T? data) => WriteAsync(message, type, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WriteAsync(message, LogType.Info, mode, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, LogMode mode, T? data) => WriteAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, T? data, CancellationToken cancellationToken) => WriteAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync<T>(string message, T? data) => WriteAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WriteAsync(string message, LogType type, LogMode mode, CancellationToken cancellationToken) => WriteAsync<object>(message, type, mode, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, LogType type, LogMode mode) => WriteAsync(message, type, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, LogType type, CancellationToken cancellationToken) => WriteAsync(message, type, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, LogType type) => WriteAsync(message, type, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, LogMode mode, CancellationToken cancellationToken) => WriteAsync(message, LogType.Info, mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, LogMode mode) => WriteAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message, CancellationToken cancellationToken) => WriteAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync(string message) => WriteAsync(message, CancellationToken.None);

    #endregion

    #region Однострочная запись

    /// <inheritdoc/>
    public virtual ValueTask WriteLineAsync<T>(ILogInfo<T> info, LogMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info, nameof(info));
        info.Message += Environment.NewLine;
        return WriteAsync(info, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(ILogInfo<T> info, LogMode mode) => WriteLineAsync(info, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(ILogInfo<T> info, CancellationToken cancellationToken) => WriteLineAsync(info, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(ILogInfo<T> info) => WriteLineAsync(info, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WriteLineAsync<T>(string message, LogType type, LogMode mode, T? data, CancellationToken cancellationToken)
        => WriteLineAsync(new LogInfo<T>(default, message, message, type, DateTime.UtcNow, data), mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, LogType type, LogMode mode, T? data) => WriteLineAsync(message, type, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, LogType type, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, type, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, LogType type, T? data) => WriteLineAsync(message, type, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, LogType.Info, mode, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, LogMode mode, T? data) => WriteLineAsync(message, mode, data, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, LogMode.All, data, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync<T>(string message, T? data) => WriteLineAsync(message, data, CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask WriteLineAsync(string message, LogType type, LogMode mode, CancellationToken cancellationToken) => WriteLineAsync<object>(message, type, mode, default, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, LogType type, LogMode mode) => WriteLineAsync(message, type, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, LogType type, CancellationToken cancellationToken) => WriteLineAsync(message, type, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, LogType type) => WriteLineAsync(message, type, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, LogMode mode, CancellationToken cancellationToken) => WriteLineAsync(message, LogType.Info, mode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, LogMode mode) => WriteLineAsync(message, mode, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message, CancellationToken cancellationToken) => WriteLineAsync(message, LogMode.All, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(string message) => WriteLineAsync(message, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync(CancellationToken cancellationToken) => WriteLineAsync(string.Empty, cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteLineAsync() => WriteLineAsync(CancellationToken.None);

    #endregion

    /// <inheritdoc/>
    public virtual async ValueTask ResetLineAsync(int offset, CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (consoleStream is null || !Mode.HasFlag(LogMode.Console))
        {
            locker.Release();
            return;
        }

        var str = "\x1b[1A\x1b[0K";
        for (var i = 0; i < offset; ++i) str += "\x1b[1D";

        await consoleStream.WriteAsync(Encoding.UTF8.GetBytes(str), cancellationToken).ConfigureAwait(false);
        locker.Release();
    }

    /// <inheritdoc/>
    public ValueTask ResetLineAsync(int offset) => ResetLineAsync(offset, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync(CancellationToken cancellationToken) => ResetLineAsync(0, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);
}