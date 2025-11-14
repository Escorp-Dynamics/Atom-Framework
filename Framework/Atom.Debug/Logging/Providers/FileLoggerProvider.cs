#pragma warning disable MA0106

using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет инструмент для создания файловых журналов событий.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FileLoggerProvider"/>.
/// </remarks>
/// <param name="dir">Путь к директории хранения журналов.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class FileLoggerProvider(string dir) : LoggerProvider<FileLogger>
{
    /// <summary>
    /// Путь к директории хранения журналов.
    /// </summary>
    public string Dir { get; set; } = dir;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLoggerProvider"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileLoggerProvider() : this("logs/") { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ILogger CreateLogger(string categoryName) => CreateLogger(categoryName, $"{Dir}{categoryName}_{DateTime.UtcNow.ToString("dd-MM-yyyy_hh-mm-ss", CultureInfo.InvariantCulture)}.log");

    /// <summary>
    /// Создаёт журнал событий.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    /// <param name="path">Путь к файлу журнала.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ILogger CreateLogger(string categoryName, string path) => Loggers.GetOrAdd(categoryName, cn => new FileLogger(cn, path));
}