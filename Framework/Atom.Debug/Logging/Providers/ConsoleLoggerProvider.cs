using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет инструмент для создания консольных журналов событий.
/// </summary>
public class ConsoleLoggerProvider : LoggerProvider<ConsoleLogger>
{
    /// <inheritdoc/>
    public override ILogger CreateLogger(string categoryName) => Loggers.GetOrAdd(categoryName, cn => new ConsoleLogger(cn));
}