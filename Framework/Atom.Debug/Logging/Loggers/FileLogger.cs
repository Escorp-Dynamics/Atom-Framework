using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет запись событий в файл.
/// </summary>
public class FileLogger : ConsoleLogger
{
    private readonly StreamWriter fileWriter;
    private bool isWriterDisposed;

    /// <summary>
    /// Путь к файлу журнала.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLogger"/>.
    /// </summary>
    /// <param name="path">Путь для хранения файла журнала событий.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileLogger(string path) : this(string.Empty, path) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLogger"/>.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    /// <param name="path">Путь для хранения файла журнала событий.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileLogger(string categoryName, string path) : base(categoryName)
    {
        Path = path;
        IsDateEnabled = true;
        IsStylingOutputEnabled = default;

        fileWriter = CreateWriter();
        Writer = fileWriter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StreamWriter CreateWriter()
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory!);

        var sw = new StreamWriter(Path, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.Append,
            Options = FileOptions.Asynchronous,
            Share = FileShare.Read,
        })
        {
            AutoFlush = true,
        };

        return sw;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void FormatDateTime([NotNull] StringBuilder sb, DateTime dt)
    {
        if (!IsDateEnabled && !IsTimeEnabled) return;
        if (IsDateEnabled) sb.Append(dt.ToString(DateFormat, CultureInfo.InvariantCulture));

        if (IsTimeEnabled)
        {
            if (IsDateEnabled) sb.Append(' ');
            sb.Append(dt.ToString(TimeFormat, CultureInfo.InvariantCulture));
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override string? CreateMessage<TState>([NotNull] LoggerEventArgs<TState> args)
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();
        var msg = base.CreateMessage(args);
        var prefix = GetPrefixByLevel(args.Level);

        sb.Append(prefix.PadLeft(8)).Append(' ').Append(msg);

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetPrefixByLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => string.Empty,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (disposing && !Interlocked.CompareExchange(ref isWriterDisposed, value: true, comparand: default)) fileWriter.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Представляет запись событий в файл.
/// </summary>
/// <typeparam name="TCategoryName">Имя категории.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FileLogger{TCategoryName}"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class FileLogger<TCategoryName>() : FileLogger(typeof(TCategoryName).FullName ?? string.Empty), ILogger<TCategoryName> { }