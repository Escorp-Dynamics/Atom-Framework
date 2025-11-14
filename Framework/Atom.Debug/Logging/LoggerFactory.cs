#pragma warning disable IDISP005

using System.Collections.Concurrent;
using Atom.Architect.Factories;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет фабрику журналов событий.
/// </summary>
public class LoggerFactory : IFactory, ILoggerFactory
{
    private readonly ConcurrentBag<ILoggerProvider> loggerProviders = [];
    private bool isDisposed;

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        if (disposing)
        {
            foreach (var provider in loggerProviders) provider.Dispose();
            loggerProviders.Clear();
        }
    }

    /// <summary>
    /// Добавляет провайдера журналов событий.
    /// </summary>
    /// <param name="provider">Провайдер журналов событий.</param>
    public void AddProvider(ILoggerProvider provider) => loggerProviders.Add(provider);

    /// <summary>
    /// Создаёт журнал событий.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    public virtual ILogger CreateLogger(string categoryName)
    {
        if (loggerProviders.IsEmpty) throw new InvalidOperationException("Не добавлено ни одного провайдера журналов событий.");

        if (loggerProviders.Count is 1) return loggerProviders.First().CreateLogger(categoryName);

        var logger = new CombinedLogger();
        foreach (var provider in loggerProviders) logger.Add(provider.CreateLogger(categoryName));
        return logger;
    }

    /// <summary>
    /// Создаёт журнал событий.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    /// <param name="path">Путь к файлу журнала.</param>
    public ILogger CreateLogger(string categoryName, string path)
    {
        if (loggerProviders.IsEmpty) throw new InvalidOperationException("Не добавлено ни одного провайдера журналов событий.");

        if (loggerProviders.Count is 1)
        {
            return loggerProviders.First() is FileLoggerProvider flp
                ? flp.CreateLogger(categoryName, path)
                : loggerProviders.First().CreateLogger(categoryName);
        }

        var logger = new CombinedLogger();

        foreach (var provider in loggerProviders)
        {
            var l = loggerProviders.First() is FileLoggerProvider flp
                ? flp.CreateLogger(categoryName, path)
                : provider.CreateLogger(categoryName);

            logger.Add(l);
        }

        return logger;
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}