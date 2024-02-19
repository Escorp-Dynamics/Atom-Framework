using System.Collections;

namespace Atom.Debug;

/// <summary>
/// Методы расширений для <see cref="Debug"/>.
/// </summary>
public static class DebugExtensions
{
    internal static IReadOnlyDictionary<string, string?> ToDictionary(this Exception ex)
    {
        var error = new Dictionary<string, string?>
        {
            { "type", ex.GetType().ToString() },
            { "message", ex.Message },
            { "stackTrace", ex.StackTrace }
        };

        foreach (DictionaryEntry data in ex.Data)
        {
            var key = data.Key.ToString();
            if (string.IsNullOrEmpty(key)) continue;
            error.Add(key, data.Value?.ToString());
        }

        return error;
    }

    /// <summary>
    /// Парсит консольный цвет из строки.
    /// </summary>
    /// <param name="name">Строковое представление цвета.</param>
    /// <param name="color">Консольный цвет.</param>
    /// <returns><c>True</c>, если операция была удачной, иначе <c>false</c>.</returns>
    public static bool TryGetColor(this string? name, out ConsoleColor? color)
    {
        color = name?.ToUpperInvariant() switch
        {
            "RED" or "R" => ConsoleColor.Red,
            "DARKRED" or "DR" => ConsoleColor.DarkRed,
            "GREEN" or "G" => ConsoleColor.Green,
            "DARKGREEN" or "DG" => ConsoleColor.DarkGreen,
            "YELLOW" or "Y" => ConsoleColor.Yellow,
            "DARKYELLOW" or "DY" => ConsoleColor.DarkYellow,
            "MAGENTA" or "M" => ConsoleColor.Magenta,
            "DARKMAGENTA" or "DM" => ConsoleColor.DarkMagenta,
            "GRAY" or "GR" => ConsoleColor.Gray,
            "DARKGRAY" or "DGR" => ConsoleColor.DarkGray,
            "CYAN" or "C" => ConsoleColor.Cyan,
            "DARKCYAN" or "DC" => ConsoleColor.DarkCyan,
            "BLUE" or "B" => ConsoleColor.Blue,
            "DARKBLUE" or "DB" => ConsoleColor.DarkBlue,
            "WHITE" or "W" => ConsoleColor.White,
            "BLACK" or "BL" => ConsoleColor.Black,
            _ => null,
        };

        return color is not null;
    }

    /// <summary>
    /// Преобразует <see cref="ConsoleColor"/> в строковое представление консольного цвета.
    /// </summary>
    /// <param name="color">Консольный цвет.</param>
    /// <param name="isBackground">Указывает, является ли цвет фоновым.</param>
    /// <returns>Строковое представление консольного цвета.</returns>
    public static string AsString(this ConsoleColor color, bool isBackground)
    {
        if (Console.IsOutputRedirected) return string.Empty;
        
        if (isBackground) return color switch
        {
            ConsoleColor.DarkRed => "\x1b[41m",
            ConsoleColor.DarkGreen => "\x1b[42m",
            ConsoleColor.DarkYellow => "\x1b[43m",
            ConsoleColor.DarkBlue => "\x1b[44m",
            ConsoleColor.DarkMagenta => "\x1b[45m",
            ConsoleColor.DarkCyan => "\x1b[46m",
            ConsoleColor.Gray => "\x1b[47m",
            ConsoleColor.DarkGray => "\x1b[100m",
            ConsoleColor.Red => "\x1b[101m",
            ConsoleColor.Green => "\x1b[102m",
            ConsoleColor.Yellow => "\x1b[103m",
            ConsoleColor.Blue => "\x1b[104m",
            ConsoleColor.Magenta => "\x1b[105m",
            ConsoleColor.Cyan => "\x1b[106m",
            ConsoleColor.White => "\x1b[107m",
            _ => "\x1b[40m",
        };

        return color switch
        {
            ConsoleColor.DarkRed => "\x1b[31m",
            ConsoleColor.DarkGreen => "\x1b[32m",
            ConsoleColor.DarkYellow => "\x1b[33m",
            ConsoleColor.DarkBlue => "\x1b[34m",
            ConsoleColor.DarkMagenta => "\x1b[35m",
            ConsoleColor.DarkCyan => "\x1b[36m",
            ConsoleColor.Gray => "\x1b[37m",
            ConsoleColor.DarkGray => "\x1b[90m",
            ConsoleColor.Red => "\x1b[91m",
            ConsoleColor.Green => "\x1b[92m",
            ConsoleColor.Yellow => "\x1b[93m",
            ConsoleColor.Blue => "\x1b[94m",
            ConsoleColor.Magenta => "\x1b[95m",
            ConsoleColor.Cyan => "\x1b[96m",
            ConsoleColor.White => "\x1b[97m",
            _ => "\x1b[30m",
        };
    }

    /// <summary>
    /// Преобразует <see cref="ConsoleColor"/> в строковое представление консольного цвета.
    /// </summary>
    /// <param name="color">Консольный цвет.</param>
    /// <returns>Строковое представление консольного цвета.</returns>
    public static string AsString(this ConsoleColor color) => color.AsString(default);

    /// <summary>
    /// Преобразует строковое представление стиля консоли в <see cref="ConsoleColor"/>.
    /// </summary>
    /// <param name="style">Строковое представление стиля консоли.</param>
    /// <returns>Стиль консоли.</returns>
    public static ConsoleStyle AsConsoleStyle(this string? style) => style?.ToUpperInvariant() switch
    {
        "BOLD" or "SB" => ConsoleStyle.Bold,
        "UNDERLINE" or "SU" => ConsoleStyle.Underline,
        "REVERSE" or "SR" => ConsoleStyle.Reverse,
        _ => default,
    };

    /// <summary>
    /// Парсит консольный стиль из строки.
    /// </summary>
    /// <param name="name">Строковое представление стиля.</param>
    /// <param name="style">Консольный стиль.</param>
    /// <returns><c>True</c>, если операция была удачной, иначе <c>false</c>.</returns>
    public static bool TryGetStyle(this string? name, out ConsoleStyle? style)
    {
        style = name?.ToUpperInvariant() switch
        {
            "BOLD" or "SB" => ConsoleStyle.Bold,
            "UNDERLINE" or "SU" => ConsoleStyle.Underline,
            "REVERSE" or "SR" => ConsoleStyle.Reverse,
            _ => null,
        };

        return style is not null;
    }

    /// <summary>
    /// Преобразует <see cref="ConsoleStyle"/> в строковое представление консольного стиля.
    /// </summary>
    /// <param name="style">Консольный стиль.</param>
    /// <param name="isEnding">Указывает, является ли тег стиля закрывающим.</param>
    /// <returns>Строковое представление консольного стиля.</returns>
    public static string AsString(this ConsoleStyle style, bool isEnding)
    {
        if (Console.IsOutputRedirected) return string.Empty;

        if (isEnding) return style switch
        {
            ConsoleStyle.Bold => "\x1b[22m",
            ConsoleStyle.Underline => "\x1b[24m",
            ConsoleStyle.Reverse => "\x1b[27m",
            _ => string.Empty,
        };

        return style switch
        {
            ConsoleStyle.Bold => "\x1b[1m",
            ConsoleStyle.Underline => "\x1b[4m",
            ConsoleStyle.Reverse => "\x1b[7m",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Преобразует <see cref="ConsoleStyle"/> в строковое представление консольного стиля.
    /// </summary>
    /// <param name="style">Консольный стиль.</param>
    /// <returns>Строковое представление консольного стиля.</returns>
    public static string AsString(this ConsoleStyle style) => style.AsString(default);
}