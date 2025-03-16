using System.Diagnostics.CodeAnalysis;
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
    /// <summary>
    /// Путь к файлу журнала.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLogger"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileLogger() : base() => IsDateEnabled = true;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="FileLogger"/>.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FileLogger(string categoryName) : base(categoryName) => IsDateEnabled = true;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override TextWriter CreateWriter()
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory!);

        var sw = File.AppendText(Path);
        sw.AutoFlush = true;
        return sw;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void FormatDateTime([NotNull] ref StringBuilder sb, DateTime dt)
    {
        if (IsDateEnabled || IsTimeEnabled)
        {
            if (IsDateEnabled) sb.Append(dt.ToString(DateFormat));

            if (IsTimeEnabled)
            {
                if (IsDateEnabled) sb.Append(' ');
                sb.Append(dt.ToString(TimeFormat));
            }
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