using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Buffers;
using Atom.Threading;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет инструментарий для работы с логированием.
/// </summary>
public abstract class Logger : ILogger, IDisposable
{
    private const string DefaultDateFormat = "dd.MM.yyyy";
    private const string DefaultTimeFormat = "HH:mm:ss.fff";

    private readonly Locker locker = new();

    private readonly Dictionary<LogLevel, bool> logLevels = new()
    {
        { LogLevel.None, default },
        { LogLevel.Trace, true },
        { LogLevel.Debug, true },
        { LogLevel.Information, true },
        { LogLevel.Warning, true },
        { LogLevel.Error, true },
        { LogLevel.Critical, true },
    };

    internal readonly AsyncLocal<ScopeContext?> scope = new();

    private readonly Locker trigger = new(0);
    private readonly ConcurrentQueue<Func<ValueTask>> queue = [];
    private readonly CancellationTokenSource cts = new();

    private bool isDisposed;

    /// <summary>
    /// Название категории.
    /// </summary>
    public string CategoryName { get; protected set; } = string.Empty;

    /// <summary>
    /// Определяет, будет ли выводиться название категории.
    /// </summary>
    public virtual bool IsCategoryNameEnabled { get; set; }

    /// <summary>
    /// Определяет, будет ли выводиться дата события.
    /// </summary>
    public virtual bool IsDateEnabled { get; set; }

    /// <summary>
    /// Определяет, будет ли выводиться время события.
    /// </summary>
    public virtual bool IsTimeEnabled { get; set; }

    /// <summary>
    /// Определяет, будет ли выводиться идентификатор события.
    /// </summary>
    public virtual bool IsEventIdEnabled { get; set; }

    /// <summary>
    /// Определяет, будет ли поддерживаться форматирование цветов и стилей.
    /// </summary>
    public virtual bool IsStylingEnabled { get; set; }

    /// <summary>
    /// Определяет, будут ли выводиться отформатированные теги цветов и стилей.
    /// </summary>
    public virtual bool IsStylingOutputEnabled { get; set; }

    /// <summary>
    /// Формат вывода даты.
    /// </summary>
    public string DateFormat { get; set; } = DefaultDateFormat;

    /// <summary>
    /// Формат вывода времени.
    /// </summary>
    public string TimeFormat { get; set; } = DefaultTimeFormat;

    /// <summary>
    /// Происходит в момент записи в журнал.
    /// </summary>
    public event MutableEventHandler<ILogger, MutableEventArgs>? Logged;

    /// <summary>
    /// Фабрика журналов событий.
    /// </summary>
    public static LoggerFactory Factory { get; } = new();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Logger"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Logger() => Task.Factory.StartNew(ProcessLogs, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Logger"/>.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Logger(string categoryName) : this()
    {
        CategoryName = categoryName;
        Task.Factory.StartNew(ProcessLogs, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Logger() => Factory.AddProvider(new ConsoleLoggerProvider());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task ProcessLogs()
    {
        while (!cts.Token.IsCancellationRequested)
        {
            await trigger.WaitAsync(cts.Token).ConfigureAwait(false);

            if (!queue.TryDequeue(out var log)) continue;

            await log().ConfigureAwait(false);
            trigger.Release();
        }
    }

    /// <summary>
    /// Происходит в момент высвобождения контекста логирования.
    /// </summary>
    /// <param name="sender">Источник.</param>
    /// <param name="e">Аргументы события.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnScopeDisposed(object? sender, [NotNull] ScopeContextEventArgs e)
    {
        scope.Value = e.Scope.Parent;
        e.Scope.Disposed -= OnScopeDisposed;

        ObjectPool<ScopeContext>.Shared.Return(e.Scope);
    }

    /// <summary>
    /// Происходит в момент записи в журнал.
    /// </summary>
    /// <param name="args">Аргументы события.</param>
    /// <typeparam name="TState">Тип связанных данных.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnLogged<TState>(LoggerEventArgs<TState> args) => Logged?.Invoke(this, args);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, высвобождаются ли управляемые ресурсы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (!disposing) return;

        cts.Cancel();
        trigger.Release();

        cts.Dispose();
        trigger.Dispose();

        locker.Dispose();
    }

    /// <summary>
    /// Включает уровни логирования.
    /// </summary>
    /// <param name="logLevels">Уровни логирования.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithLogLevels([NotNull] params IEnumerable<LogLevel> logLevels)
    {
        foreach (var logLevel in logLevels) this.logLevels[logLevel] = true;
        return this;
    }

    /// <summary>
    /// Выключает уровни логирования.
    /// </summary>
    /// <param name="logLevels">Уровни логирования.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithoutLogLevels([NotNull] params IEnumerable<LogLevel> logLevels)
    {
        foreach (var logLevel in logLevels) this.logLevels[logLevel] = default;
        return this;
    }

    /// <summary>
    /// Указывает, выводится ли название категории при логировании.
    /// </summary>
    /// <param name="display">Выводить ли название категории в журнал.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithCategoryName(bool display)
    {
        IsCategoryNameEnabled = display;
        return this;
    }

    /// <summary>
    /// Включает вывод названия категории в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithCategoryName() => WithCategoryName(true);

    /// <summary>
    /// Отключает вывод названия категории в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithoutCategoryName() => WithCategoryName(default);

    /// <summary>
    /// Указывает, выводится ли дата события при логировании.
    /// </summary>
    /// <param name="display">Выводить ли дату события в журнал.</param>
    /// <param name="format">Формат вывода даты.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithDate(bool display, string format)
    {
        IsDateEnabled = display;
        DateFormat = format;
        return this;
    }

    /// <summary>
    /// Включает вывод даты события в журнал.
    /// </summary>
    /// <param name="format">Формат вывода даты.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithDate(string format) => WithDate(true, format);

    /// <summary>
    /// Включает вывод даты события в журнал.
    /// </summary>
    /// <param name="display">Выводить ли дату события в журнал.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithDate(bool display) => WithDate(display, DefaultDateFormat);

    /// <summary>
    /// Включает вывод даты события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithDate() => WithDate(true);

    /// <summary>
    /// Отключает вывод даты события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithoutDate() => WithDate(false);

    /// <summary>
    /// Указывает, выводится ли время события при логировании.
    /// </summary>
    /// <param name="display">Выводить ли время события в журнал.</param>
    /// <param name="format">Формат вывода времени.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithTime(bool display, string format)
    {
        IsTimeEnabled = display;
        TimeFormat = format;
        return this;
    }

    /// <summary>
    /// Включает вывод времени события в журнал.
    /// </summary>
    /// <param name="format">Формат вывода времени.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithTime(string format) => WithTime(true, format);

    /// <summary>
    /// Включает вывод времени события в журнал.
    /// </summary>
    /// <param name="display">Выводить ли время события в журнал.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithTime(bool display) => WithTime(display, DefaultTimeFormat);

    /// <summary>
    /// Включает вывод времени события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithTime() => WithTime(true);

    /// <summary>
    /// Отключает вывод времени события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithoutTime() => WithTime(false);

    /// <summary>
    /// Указывает, выводится ли идентификатор события при логировании.
    /// </summary>
    /// <param name="display">Выводить ли идентификатор события в журнал.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithEventId(bool display)
    {
        IsEventIdEnabled = display;
        return this;
    }

    /// <summary>
    /// Включает вывод идентификатора события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithEventId() => WithEventId(true);

    /// <summary>
    /// Отключает вывод идентификатора события в журнал.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithoutEventId() => WithEventId(default);

    /// <summary>
    /// Указывает, поддерживается ли форматирование цветов и стилей.
    /// </summary>
    /// <param name="isEnabled">Выводить ли идентификатор события в журнал.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Logger WithStyling(bool isEnabled)
    {
        IsStylingEnabled = isEnabled;
        return this;
    }

    /// <summary>
    /// Включает форматирование цветов и стилей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithStyling() => WithStyling(true);

    /// <summary>
    /// Отключает форматирование цветов и стилей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Logger WithoutStyling() => WithStyling(default);

    /// <summary>
    /// Открывает область видимости нового контекста для логирования.
    /// </summary>
    /// <param name="state">Данные состояния контекста.</param>
    /// <typeparam name="TState">Тип данных состояния контекста.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var ctx = ObjectPool<ScopeContext>.Shared.Rent();

        ctx.State = state;
        ctx.Parent = scope.Value;
        ctx.ThreadId = Environment.CurrentManagedThreadId;
        ctx.TaskId = Task.CurrentId;
        ctx.Disposed += OnScopeDisposed;

        return scope.Value = ctx;
    }

    /// <summary>
    /// Определяет, включён ли уровень логирования.
    /// </summary>
    /// <param name="logLevel">Уровень логирования.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel logLevel) => logLevels[logLevel];

    /// <summary>
    /// Отправляет сообщение в журнал.
    /// </summary>
    /// <param name="logLevel">Уровень логирования.</param>
    /// <param name="eventId">Идентификатор события логирования.</param>
    /// <param name="state">Данные состояния контекста.</param>
    /// <param name="exception">Связанное исключение.</param>
    /// <param name="formatter">Функция форматирования.</param>
    /// <typeparam name="TState">Тип данных состояния контекста.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, [NotNull] Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || IsEnabled(LogLevel.None)) return;

        var e = MutableEventArgs.Rent<LoggerEventArgs<TState>>();

        e.Scope = scope.Value?.ToString();
        e.Level = logLevel;
        e.EventId = eventId;
        e.State = state;
        e.Exception = exception;
        e.Formatter = formatter;
        e.DateTime = DateTime.UtcNow;

        e.Reset();

        queue.Enqueue(async () =>
        {
            await locker.WaitAsync().ConfigureAwait(false);

            try
            {
                OnLogged(e);
            }
            finally
            {
                locker.Release();
                MutableEventArgs.Return(e);
            }
        });

        trigger.Release();
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Представляет инструментарий работы с логированием.
/// </summary>
/// <typeparam name="TCategoryName">Имя категории.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Logger{TCategoryName}"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract class Logger<TCategoryName>() : Logger(typeof(TCategoryName).FullName ?? string.Empty), ILogger<TCategoryName> { }