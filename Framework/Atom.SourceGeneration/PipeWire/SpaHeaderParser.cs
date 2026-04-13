using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Atom.SourceGeneration.PipeWire;

/// <summary>
/// Парсит C enum определения из SPA (Simple Plugin API) заголовочных файлов PipeWire.
/// </summary>
internal static partial class SpaHeaderParser
{
    [GeneratedRegex(@"enum\s+(?<name>\w+)\s*\{(?<body>.*?)\}", RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex EnumPattern { get; }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex CCommentPattern { get; }

    [GeneratedRegex(@"^[A-Za-z_]\w*$", RegexOptions.NonBacktracking)]
    private static partial Regex IdentifierPattern { get; }

    /// <summary>
    /// Представляет один член C enum.
    /// </summary>
    /// <param name="Name">Имя члена.</param>
    /// <param name="Value">Числовое значение.</param>
    internal readonly record struct EnumEntry(string Name, uint Value);

    /// <summary>
    /// Парсит C enum из текста заголовочного файла.
    /// Поддерживает hex/decimal значения, авто-инкремент и пропускает алиасы.
    /// </summary>
    /// <param name="headerText">Текст .h файла, содержащий один enum.</param>
    /// <returns>Имя enum и его члены.</returns>
    internal static (string EnumName, List<EnumEntry> Entries) Parse(string headerText)
    {
        var enumMatch = EnumPattern.Match(headerText);
        if (!enumMatch.Success)
        {
            return (string.Empty, []);
        }

        var enumName = enumMatch.Groups["name"].Value;
        var body = CCommentPattern.Replace(enumMatch.Groups["body"].Value, string.Empty);
        var entries = new List<EnumEntry>();
        var currentValue = 0u;

        foreach (var rawLine in body.Split('\n'))
        {
            ParseLine(rawLine, entries, ref currentValue);
        }

        return (enumName, entries);
    }

    private static void ParseLine(string rawLine, List<EnumEntry> entries, ref uint currentValue)
    {
        var line = CCommentPattern.Replace(rawLine, string.Empty);

        var cppCommentIdx = line.IndexOf("//", StringComparison.Ordinal);
        if (cppCommentIdx >= 0)
        {
            line = line[..cppCommentIdx];
        }

        line = line.Trim().TrimEnd(',');

        if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal)
            || line.StartsWith("/*", StringComparison.Ordinal))
        {
            return;
        }

        if (line.Contains('='))
        {
            ParseAssignment(line, entries, ref currentValue);
        }
        else
        {
            var name = line.Trim();
            if (name.Length > 0 && char.IsLetter(name[0]))
            {
                entries.Add(new EnumEntry(name, currentValue));
                currentValue++;
            }
        }
    }

    private static void ParseAssignment(string line, List<EnumEntry> entries, ref uint currentValue)
    {
        var parts = line.Split('=', 2);
        var name = parts[0].Trim();
        var valStr = parts[1].Trim();

        // Пропускаем алиасы (ссылки на другие enum members)
        if (IdentifierPattern.IsMatch(valStr))
        {
            return;
        }

        if (!TryParseValue(valStr, out currentValue))
        {
            return;
        }

        entries.Add(new EnumEntry(name, currentValue));
        currentValue++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseValue(string valStr, out uint result)
    {
        if (valStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(valStr.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return uint.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
