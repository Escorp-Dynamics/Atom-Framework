using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет инструмент для создания файловых журналов событий.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FileLoggerProvider"/>.
/// </remarks>
/// <param name="dir">Путь к директории хранения журналов.</param>
public class FileLoggerProvider(string dir) : LoggerProvider<FileLogger>
{
    /// <summary>
    /// Путь к директории хранения журналов.
    /// </summary>
    public string Dir { get; set; } = dir;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLoggerProvider"/>.
    /// </summary>
    public FileLoggerProvider() : this("logs/") { }

    /// <inheritdoc/>
    public override ILogger CreateLogger(string categoryName) => CreateLogger(categoryName, $"{Dir}{categoryName}_{DateTime.UtcNow:dd-MM-yyyy_hh-mm-ss}.log");

    /// <summary>
    /// Создаёт журнал событий.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    /// <param name="path">Путь к файлу журнала.</param>
    public ILogger CreateLogger(string categoryName, string path) => Loggers.GetOrAdd(categoryName, cn => new FileLogger(cn) { Path = path });
}