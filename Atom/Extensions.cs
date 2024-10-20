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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char[] CreateTable(bool upper = default, bool invariant = default)
    {
        var table = new char[ushort.MaxValue + 1];
        Func<char, char> handler;

        if (upper)
            handler = invariant ? char.ToUpperInvariant : char.ToUpper;
        else
            handler = invariant ? char.ToLowerInvariant : char.ToLower;

        for (var i = 0; i < table.Length; ++i) table[i] = handler((char)i);

        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char GetChar(char c, char[] table) => c >= 'a' && c <= 'z' ? (char)(c - 'a' + 'A') : table[c];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetGenericName(Type type, string name, bool withNamespaces)
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
            sb.Append(GetFriendlyName(genericArgs[i], withNamespaces));
        }

        if (name is not "System.Nullable" and not "Nullable") sb.Append('>');

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return result;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetFriendlyName([NotNull] this Type type, bool withNamespaces)
    {
        var name = withNamespaces && !string.IsNullOrEmpty(type.FullName) ? type.FullName : type.Name;
        if (!type.IsGenericType) return type.IsNullable() ? name + '?' : name;

        var genericName = GetGenericName(type, name, withNamespaces);
        return type.IsNullable() ? genericName + '?' : genericName;
    }

    /// <summary>
    /// Получает имя типа.
    /// </summary>
    /// <param name="type">Метаданные типа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetFriendlyName([NotNull] this Type type) => type.GetFriendlyName(true);
}