#pragma warning disable IDISP001, IDISP003, IDISP008, CA2213
using System.Runtime.CompilerServices;
using Atom.Buffers;

namespace Atom.Text;

internal static class UnixStyleFormatter
{
    public static string? Format(string? source, bool removeFormatting)
    {
        if (string.IsNullOrEmpty(source)) return source;

        var outText = new ValueStringBuilder(source.Length * 2);
        var parsedData = new ValueStringBuilder(32);
        var foregrounds = ObjectPool<Stack<ConsoleColor>>.Shared.Rent();
        var backgrounds = ObjectPool<Stack<ConsoleColor>>.Shared.Rent();

        foregrounds.Push(Console.ForegroundColor);
        backgrounds.Push(Console.BackgroundColor);

        ParseSource(source, ref outText, ref parsedData, ref foregrounds, ref backgrounds, removeFormatting);
        var result = FinalizeOutput(ref outText, removeFormatting);

        ObjectPool<Stack<ConsoleColor>>.Shared.Return(backgrounds, static x => x.Clear());
        ObjectPool<Stack<ConsoleColor>>.Shared.Return(foregrounds, static x => x.Clear());
        outText.Dispose();
        parsedData.Dispose();

        return result;
    }

    public static string? Format(string? source) => Format(source, default);

    private static void ParseSource(
        string source,
        ref ValueStringBuilder outText,
        ref ValueStringBuilder parsedData,
        ref Stack<ConsoleColor> foregrounds,
        ref Stack<ConsoleColor> backgrounds,
        bool removeFormatting)
    {
        var isParsing = false;
        var isClosing = false;
        var textStart = 0;

        for (var i = 0; i < source.Length; ++i)
        {
            var c = source[i];
            textStart = ProcessChar(c, i, source, ref outText, ref parsedData, ref foregrounds, ref backgrounds,
                                    ref isParsing, ref isClosing, textStart, removeFormatting);
        }

        // Если парсинг тега не завершён (нет закрывающей ']') — выводим как есть
        if (isParsing)
        {
            // Содержимое после '[' уже в parsedData, поэтому не добавляем textStart..end
            outText.Append('[');
            if (isClosing) outText.Append('/');
            outText.Append(parsedData.AsSpan());
        }
        else if (textStart < source.Length)
        {
            // Обычный текст после последнего обработанного тега
            outText.Append(source.AsSpan(textStart, source.Length - textStart));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ProcessChar(
        char c, int i, string source,
        ref ValueStringBuilder outText, ref ValueStringBuilder parsedData,
        ref Stack<ConsoleColor> foregrounds, ref Stack<ConsoleColor> backgrounds,
        ref bool isParsing, ref bool isClosing, int textStart, bool removeFormatting)
    {
        return c switch
        {
            '[' => HandleOpenBracket(i, source, ref outText, ref parsedData, ref isParsing, ref isClosing, textStart),
            ']' when isParsing => HandleCloseBracket(i, ref outText, ref parsedData, ref foregrounds, ref backgrounds, ref isParsing, ref isClosing, removeFormatting),
            _ when isParsing => HandleParseChar(c, ref parsedData, ref isClosing, textStart),
            _ => textStart,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HandleOpenBracket(
        int i, string source, ref ValueStringBuilder outText, ref ValueStringBuilder parsedData,
        ref bool isParsing, ref bool isClosing, int textStart)
    {
        if (!isParsing && i > textStart)
            outText.Append(source.AsSpan(textStart, i - textStart));

        // Если уже парсим тег и встретили '[' — выводим незавершённый тег как текст
        if (isParsing)
        {
            outText.Append('[');
            if (isClosing) outText.Append('/');
            outText.Append(parsedData.AsSpan());
            parsedData.Clear();
            isClosing = false;
        }

        isParsing = true;
        return i + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HandleCloseBracket(
        int i, ref ValueStringBuilder outText, ref ValueStringBuilder parsedData,
        ref Stack<ConsoleColor> foregrounds, ref Stack<ConsoleColor> backgrounds,
        ref bool isParsing, ref bool isClosing, bool removeFormatting)
    {
        ProcessTag(ref outText, ref parsedData, ref foregrounds, ref backgrounds, isClosing, removeFormatting);
        isParsing = false;
        isClosing = false;
        parsedData.Clear();
        return i + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HandleParseChar(char c, ref ValueStringBuilder parsedData, ref bool isClosing, int textStart)
    {
        if (c is '/' && parsedData.Length is 0) isClosing = true;
        else parsedData.Append(c);
        return textStart;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string FinalizeOutput(ref ValueStringBuilder outText, bool removeFormatting)
    {
        if (removeFormatting) return outText.ToString();

        outText.Insert(0, "\x1b[0m");
        outText.Append("\x1b[0m");
        return outText.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessTag(
        ref ValueStringBuilder outText,
        ref ValueStringBuilder parsedData,
        ref Stack<ConsoleColor> foregrounds,
        ref Stack<ConsoleColor> backgrounds,
        bool isClosing,
        bool removeFormatting)
    {
        // Пустой тег [] или [/] — выводим как есть
        if (parsedData.Length is 0)
        {
            outText.Append(isClosing ? "[/]" : "[]");
            return;
        }

        var data = parsedData.AsSpan();
        var colonIdx = data.IndexOf(':');

        var first = colonIdx >= 0 ? data[..colonIdx].Trim() : data.Trim();
        var second = colonIdx >= 0 ? data[(colonIdx + 1)..].Trim() : [];

        // Тег только с пробелами [ ] — выводим как есть
        if (first.IsEmpty)
        {
            AppendUnrecognizedTag(ref outText, data, isClosing);
            return;
        }

        if (first.TryGetColor(out var color))
        {
            HandleForegroundColor(ref outText, ref foregrounds, color, isClosing, data, removeFormatting);
            if (!second.IsEmpty && second.TryGetColor(out var bgColor))
                HandleBackgroundColor(ref outText, ref backgrounds, bgColor, isClosing, data, removeFormatting);
        }
        else if (first.TryGetStyle(out var style) && style is not default(ConsoleStyle))
        {
            HandleStyle(ref outText, style, isClosing, removeFormatting);
        }
        else
        {
            AppendUnrecognizedTag(ref outText, data, isClosing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleForegroundColor(
        ref ValueStringBuilder outText, ref Stack<ConsoleColor> foregrounds,
        ConsoleColor color, bool isClosing, ReadOnlySpan<char> data, bool removeFormatting)
    {
        if (isClosing)
        {
            if (color == foregrounds.Peek())
            {
                foregrounds.Pop();
                if (!removeFormatting) outText.Append(foregrounds.Peek().AsString());
            }
            else if (!removeFormatting)
            {
                AppendClosingTag(ref outText, data);
            }
        }
        else
        {
            foregrounds.Push(color);
            if (!removeFormatting) outText.Append(color.AsString());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBackgroundColor(
        ref ValueStringBuilder outText, ref Stack<ConsoleColor> backgrounds,
        ConsoleColor color, bool isClosing, ReadOnlySpan<char> data, bool removeFormatting)
    {
        if (isClosing)
        {
            if (color == backgrounds.Peek())
            {
                backgrounds.Pop();
                if (!removeFormatting) outText.Append(backgrounds.Peek().AsString(isBackground: true));
            }
            else if (!removeFormatting)
            {
                AppendClosingTag(ref outText, data);
            }
        }
        else
        {
            backgrounds.Push(color);
            if (!removeFormatting) outText.Append(color.AsString(isBackground: true));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStyle(ref ValueStringBuilder outText, ConsoleStyle style, bool isClosing, bool removeFormatting)
    {
        if (!removeFormatting) outText.Append(style.AsString(isEnding: isClosing));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendUnrecognizedTag(ref ValueStringBuilder outText, ReadOnlySpan<char> data, bool isClosing)
    {
        outText.Append('[');
        if (isClosing) outText.Append('/');
        outText.Append(data);
        outText.Append(']');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendClosingTag(ref ValueStringBuilder outText, ReadOnlySpan<char> data)
    {
        outText.Append("[/");
        outText.Append(data);
        outText.Append(']');
    }
}
