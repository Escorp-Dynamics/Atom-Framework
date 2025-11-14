using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;
using Atom.Text;
using Microsoft.Extensions.Logging;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет запись событий в консоль.
/// </summary>
public class ConsoleLogger : Logger
{
    /// <summary>
    /// Поток записи.
    /// </summary>
    public TextWriter Writer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref field);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Volatile.Write(ref field, value);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConsoleLogger"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ConsoleLogger() : base()
    {
        IsTimeEnabled = IsStylingEnabled = IsStylingOutputEnabled = true;
        Writer = Console.Out;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConsoleLogger"/>.
    /// </summary>
    /// <param name="categoryName">Название категории.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ConsoleLogger(string categoryName) : base(categoryName)
    {
        IsTimeEnabled = IsStylingEnabled = IsStylingOutputEnabled = true;
        Writer = Console.Out;
    }

    /// <summary>
    /// Форматирует время и дату события.
    /// </summary>
    /// <param name="sb">Ссылка на <see cref="StringBuilder"/>.</param>
    /// <param name="dt">Время и дата события.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void FormatDateTime([NotNull] StringBuilder sb, DateTime dt)
    {
        if (!IsDateEnabled && !IsTimeEnabled) return;
        if (IsDateEnabled) sb.Append(dt.ToString(DateFormat, CultureInfo.InvariantCulture));

        if (IsTimeEnabled)
        {
            if (IsDateEnabled) sb.Append(' ');
            sb.Append(dt.ToString(TimeFormat, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Создаёт итоговое сообщение журнала.
    /// </summary>
    /// <param name="args">Аргументы события журнала.</param>
    /// <typeparam name="TState">Тип связанных данных события журнала.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual string? CreateMessage<TState>([NotNull] LoggerEventArgs<TState> args)
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();
        var color = GetColorByLevel(args.Level);

        if (IsStylingEnabled) sb.Append('[').Append(color).Append(']');
        FormatDateTime(sb, args.DateTime);
        if (Console.IsOutputRedirected) sb.Append(' ').Append(GetPrefixByLevel(args.Level));

        if (IsCategoryNameEnabled && !string.IsNullOrEmpty(CategoryName)) sb.Append(' ').Append(CategoryName);
        if (IsEventIdEnabled && args.EventId.Id != default) sb.Append(' ').Append(args.EventId);
        if (!string.IsNullOrEmpty(args.Scope)) sb.Append(' ').Append(args.Scope);

        var message = string.Empty;
        if (args.Formatter is not null && args.State is not null) message = args.Formatter(args.State, args.Exception);
        if (!string.IsNullOrEmpty(message)) sb.Append(' ').Append(message);

        if (IsStylingEnabled) sb.Append("[/").Append(color).Append(']');

        message = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        if (IsStylingEnabled) message = message.ToUnixStyleFormat(!IsStylingOutputEnabled || Console.IsOutputRedirected);

        return message;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnLogged<TState>([NotNull] LoggerEventArgs<TState> args)
    {
        base.OnLogged(args);
        if (args.IsCancelled) return;

        var result = CreateMessage(args);
        if (string.IsNullOrEmpty(result)) return;

        Writer.WriteLine(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetColorByLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "dgr",
        LogLevel.Debug => "gr",
        LogLevel.Warning => "y",
        LogLevel.Error => "dr",
        LogLevel.Critical => "r",
        _ => "w",
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char GetPrefixByLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => 'T',
        LogLevel.Debug => 'D',
        LogLevel.Warning => 'W',
        LogLevel.Error => 'E',
        LogLevel.Critical => 'C',
        _ => 'I',
    };
}

/// <summary>
/// Представляет запись событий в консоль.
/// </summary>
/// <typeparam name="TCategoryName">Имя категории.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ConsoleLogger{TCategoryName}"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class ConsoleLogger<TCategoryName>() : ConsoleLogger(typeof(TCategoryName).FullName ?? string.Empty), ILogger<TCategoryName> { }