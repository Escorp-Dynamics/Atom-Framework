using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет запись событий в комбинированный журнал.
/// </summary>
public class CombinedLogger : Logger
{
    private readonly ConcurrentBag<ILogger> loggers = [];

    /// <summary>
    /// Подключённые журналы.
    /// </summary>
    public IEnumerable<ILogger> Loggers => loggers;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CombinedLogger"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CombinedLogger() : base() { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CombinedLogger"/>.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CombinedLogger(string categoryName) : base(categoryName) { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithCategoryName(bool display)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithCategoryName(display);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithDate(bool display, string format)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithDate(display, format);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithEventId(bool display)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithEventId(display);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithLogLevels([NotNull] params IEnumerable<LogLevel> logLevels)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithLogLevels(logLevels);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithoutLogLevels([NotNull] params IEnumerable<LogLevel> logLevels)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithoutLogLevels(logLevels);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithStyling(bool isEnabled)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithStyling(isEnabled);
        }

        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Logger WithTime(bool display, string format)
    {
        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.WithTime(display, format);
        }

        return this;
    }

    /// <summary>
    /// Добавляет журнал событий.
    /// </summary>
    /// <param name="logger">Журнал событий.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ILogger logger) => loggers.Add(logger);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override IDisposable? BeginScope<TState>(TState state)
    {
        var scopeContext = base.BeginScope(state) as ScopeContext;

        foreach (var logger in loggers)
        {
            if (logger is Logger l) l.scope.Value = scopeContext;
        }

        return scopeContext;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, [NotNull] Func<TState, Exception?, string> formatter)
    {
        foreach (var logger in loggers) logger.Log(logLevel, eventId, state, exception, formatter);
    }
}

/// <summary>
/// Представляет запись событий в комбинированный журнал.
/// </summary>
/// <typeparam name="TCategoryName">Имя категории.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CombinedLogger{TCategoryName}"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class CombinedLogger<TCategoryName>() : CombinedLogger(typeof(TCategoryName).FullName ?? string.Empty), ILogger<TCategoryName>;