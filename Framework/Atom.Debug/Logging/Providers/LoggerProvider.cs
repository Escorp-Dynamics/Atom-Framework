using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет инструмент для создания журналов событий.
/// </summary>
/// <typeparam name="TLogger">Тип журнала событий.</typeparam>
public abstract class LoggerProvider<TLogger> : ILoggerProvider where TLogger : Logger
{
    private bool isDisposed;

    /// <summary>
    /// Активные журналы событий.
    /// </summary>
    protected ConcurrentDictionary<string, TLogger> Loggers { get; } = [];

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        if (disposing)
        {
            foreach (var logger in Loggers.Values) logger.Dispose();
            Loggers.Clear();
        }
    }

    /// <summary>
    /// Создаёт журнал событий.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    public abstract ILogger CreateLogger(string categoryName);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}