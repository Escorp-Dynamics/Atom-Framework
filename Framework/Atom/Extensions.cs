using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;

namespace Atom;

/// <summary>
/// Представляет системные расширения.
/// </summary>
public static class Extensions
{
    private static readonly Lazy<char[]> upperCaseTable = new(() => CreateTable(true), true);
    private static readonly Lazy<char[]> invariantUpperCaseTable = new(() => CreateTable(true, true), true);
    private static readonly Lazy<char[]> lowerCaseTable = new(() => CreateTable(), true);
    private static readonly Lazy<char[]> invariantLowerCaseTable = new(() => CreateTable(default, true), true);

    private static readonly Dictionary<string, ConsoleColor> colorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RED"] = ConsoleColor.Red,
        ["R"] = ConsoleColor.Red,
        ["DARKRED"] = ConsoleColor.DarkRed,
        ["DR"] = ConsoleColor.DarkRed,
        ["GREEN"] = ConsoleColor.Green,
        ["G"] = ConsoleColor.Green,
        ["DARKGREEN"] = ConsoleColor.DarkGreen,
        ["DG"] = ConsoleColor.DarkGreen,
        ["YELLOW"] = ConsoleColor.Yellow,
        ["Y"] = ConsoleColor.Yellow,
        ["DARKYELLOW"] = ConsoleColor.DarkYellow,
        ["DY"] = ConsoleColor.DarkYellow,
        ["MAGENTA"] = ConsoleColor.Magenta,
        ["M"] = ConsoleColor.Magenta,
        ["DARKMAGENTA"] = ConsoleColor.DarkMagenta,
        ["DM"] = ConsoleColor.DarkMagenta,
        ["GRAY"] = ConsoleColor.Gray,
        ["GR"] = ConsoleColor.Gray,
        ["DARKGRAY"] = ConsoleColor.DarkGray,
        ["DGR"] = ConsoleColor.DarkGray,
        ["CYAN"] = ConsoleColor.Cyan,
        ["C"] = ConsoleColor.Cyan,
        ["DARKCYAN"] = ConsoleColor.DarkCyan,
        ["DC"] = ConsoleColor.DarkCyan,
        ["BLUE"] = ConsoleColor.Blue,
        ["B"] = ConsoleColor.Blue,
        ["DARKBLUE"] = ConsoleColor.DarkBlue,
        ["DB"] = ConsoleColor.DarkBlue,
        ["WHITE"] = ConsoleColor.White,
        ["W"] = ConsoleColor.White,
        ["BLACK"] = ConsoleColor.Black,
        ["BL"] = ConsoleColor.Black
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char[] CreateTable(bool upper = default, bool invariant = default)
    {
        var table = new char[ushort.MaxValue + 1];
        Func<char, char> upperFunc = invariant ? char.ToUpperInvariant : char.ToUpper;
        Func<char, char> lowerFunc = invariant ? char.ToLowerInvariant : char.ToLower;
        var handler = upper ? upperFunc : lowerFunc;

        for (var i = 0; i < table.Length; ++i) table[i] = handler((char)i);

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char GetChar(char c, char[] table) => c is >= 'a' and <= 'z' ? (char)(c - 'a' + 'A') : table[c];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetGenericName(Type type, string name, bool withNamespaces, bool withGenericNullable)
    {
        var index = name.IndexOf('`');
        name = index > 0 ? name[..index] : name;

        var genericArgs = type.GetGenericArguments();
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        if (name is not "System.Nullable" and not "Nullable")
        {
            sb.Append(name);
            sb.Append('<');
        }

        for (var i = 0; i < genericArgs.Length; ++i)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(GetFriendlyName(genericArgs[i], withNamespaces, withGenericNullable));
        }

        if (name is not "System.Nullable" and not "Nullable") sb.Append('>');

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string SimplifyTypeName(string name)
    {
        if (name.EndsWith("[]")) return SimplifyTypeName(name[..^2]) + "[]";

        return name switch
        {
            "System.String" or "String" => "string",
            "System.Boolean" or "Boolean" => "bool",
            "System.Char" or "Char" => "char",
            "System.SByte" or "SByte" => "sbyte",
            "System.Byte" or "Byte" => "byte",
            "System.UInt16" or "UInt16" => "ushort",
            "System.Int16" or "Int16" => "short",
            "System.UInt32" or "UInt32" => "uint",
            "System.Int32" or "Int32" => "int",
            "System.UInt64" or "UInt64" => "ulong",
            "System.Int64" or "Int64" => "long",
            "System.Single" or "Single" => "float",
            "System.Double" or "Double" => "double",
            "System.Decimal" or "Decimal" => "decimal",
            "System.Object" or "Object" => "object",
            _ => name,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static char GetLowerChar(char c, StringComparison comparison) => comparison switch
    {
        StringComparison.InvariantCultureIgnoreCase => GetChar(c, invariantLowerCaseTable.Value),
        StringComparison.CurrentCultureIgnoreCase or StringComparison.OrdinalIgnoreCase => GetChar(c, lowerCaseTable.Value),
        _ => c,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static char GetUpperChar(char c, StringComparison comparison) => comparison switch
    {
        StringComparison.InvariantCultureIgnoreCase => GetChar(c, invariantUpperCaseTable.Value),
        StringComparison.CurrentCultureIgnoreCase or StringComparison.OrdinalIgnoreCase => GetChar(c, upperCaseTable.Value),
        _ => c,
    };

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <param name="separator">Разделитель.</param>
    /// <returns>Объединённая строка.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Join(this IEnumerable<string> values, string separator) => string.Join(separator, values);

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <param name="separator">Разделитель.</param>
    /// <returns>Объединённая строка.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Join(this IEnumerable<string> values, char separator) => string.Join(separator, values);

    /// <summary>
    /// Возвращает строку, состоящую из указанных элементов, разделённых разделителем.
    /// </summary>
    /// <param name="values">Объединяемые элементы.</param>
    /// <returns>Объединённая строка.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Join(this IEnumerable<string> values) => values.Join(", ");

    /// <summary>
    /// Сравнивает два символа с учетом указанного способа сравнения.
    /// </summary>
    /// <param name="c1">Первый символ.</param>
    /// <param name="c2">Второй символ.</param>
    /// <param name="comparison">Способ сравнения.</param>
    /// <returns><c>true</c>, если символы равны, иначе <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(this char c1, char c2, StringComparison comparison) => GetUpperChar(c1, comparison) == GetUpperChar(c2, comparison);

    /// <summary>
    /// Определяет, является ли тип допускающим <c>null</c>.
    /// </summary>
    /// <param name="type">Метаданные типа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullable([NotNull] this Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    /// <summary>
    /// Получает имя типа.
    /// </summary>
    /// <param name="type">Метаданные типа.</param>
    /// <param name="withNamespaces">Указывает, нужно ли возвращать полное имя типа с пространством имён.</param>
    /// <param name="withNullable">Указывает, нужно ли возвращать имя типа с nullable-модификатором.</param>
    /// <param name="withGenericNullable">Указывает, нужно ли возвращать имя типа дженерика с nullable-модификатором.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetFriendlyName([NotNull] this Type type, bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true)
    {
        var name = withNamespaces && !string.IsNullOrEmpty(type.FullName) ? type.FullName : type.Name;

        if (!type.IsGenericType)
        {
            name = SimplifyTypeName(name);
            if (withNullable) return type.IsNullable() ? name + '?' : name;
            return name;
        }

        name = GetGenericName(type, name, withNamespaces, withGenericNullable);
        name = SimplifyTypeName(name);

        if (withNullable) return type.IsNullable() ? name + '?' : name;
        return name;
    }

    /// <summary>
    /// Преобразует экземпляр исключения в словарь.
    /// </summary>
    /// <param name="ex">Экземпляр исключения.</param>
    public static IReadOnlyDictionary<string, string?> AsDictionary([NotNull] this Exception ex)
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
    public static bool TryGetColor(this string name, out ConsoleColor color) => colorMap.TryGetValue(name, out color);

    /// <summary>
    /// Преобразует <see cref="ConsoleColor"/> в строковое представление консольного цвета.
    /// </summary>
    /// <param name="color">Консольный цвет.</param>
    /// <param name="isBackground">Указывает, является ли цвет фоновым.</param>
    /// <returns>Строковое представление консольного цвета.</returns>
    public static string AsString(this ConsoleColor color, bool isBackground)
    {
        if (Console.IsOutputRedirected) return string.Empty;

        if (isBackground)
        {
            return color switch
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
        }

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
    /// Преобразует <see cref="ConsoleStyle"/> в строковое представление консольного стиля.
    /// </summary>
    /// <param name="style">Консольный стиль.</param>
    /// <param name="isEnding">Указывает, является ли тег стиля закрывающим.</param>
    /// <returns>Строковое представление консольного стиля.</returns>
    public static string AsString(this ConsoleStyle style, bool isEnding)
    {
        if (Console.IsOutputRedirected) return string.Empty;

        if (isEnding)
        {
            return style switch
            {
                ConsoleStyle.Bold => "\x1b[22m",
                ConsoleStyle.Underline => "\x1b[24m",
                ConsoleStyle.Reverse => "\x1b[27m",
                _ => string.Empty,
            };
        }

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
    /// Выполняет обработчик события.
    /// </summary>
    /// <param name="handler">Обработчик события.</param>
    /// <param name="argsModifier">модификатор аргументов события.</param>
    /// <typeparam name="T">Тип аргументов события.</typeparam>
    /// <returns>True, если событие было успешно выполнено и не отменено, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool On<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this MutableEventHandler<T>? handler, Action<T>? argsModifier) where T : MutableEventArgs
    {
        if (handler is null) return true;

        var args = MutableEventArgs.Rent<T>();
        argsModifier?.Invoke(args);
        args.Restart();

        handler(args);

        var isCancelled = args.IsCancelled;
        MutableEventArgs.Return(args);

        return !isCancelled;
    }

    /// <summary>
    /// Выполняет обработчик события.
    /// </summary>
    /// <param name="handler">Обработчик события.</param>
    /// <typeparam name="T">Тип аргументов события.</typeparam>
    /// <returns>True, если событие было успешно выполнено и не отменено, иначе false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool On<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this MutableEventHandler<T>? handler) where T : MutableEventArgs => handler.On(default);
}