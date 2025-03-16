using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;

namespace Atom.Text;

internal static class UnixStyleFormatter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleOpeningBracket(ref bool isParsing, ref StringBuilder outText, ref StringBuilder parsedData, bool removeFormatting)
    {
        if (isParsing)
        {
            parsedData.Clear();
            if (!removeFormatting) outText.Append('[');
        }

        isParsing = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleClosingBracket(ref bool isParsing, ref bool isClosing, ref StringBuilder outText, ref StringBuilder parsedData, ref Stack<ConsoleColor> foregrounds, ref Stack<ConsoleColor> backgrounds, bool removeFormatting)
    {
        if (parsedData.Length > 0)
        {
            var tmp = parsedData.ToString().Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tmp.Length > 0) HandleColorOrStyle(ref outText, ref parsedData, ref foregrounds, ref backgrounds, tmp, isClosing, removeFormatting);
        }

        isParsing = false;
        isClosing = false;
        parsedData.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleParsingCharacter(ref bool isClosing, ref StringBuilder parsedData, string source, int i)
    {
        if (source[i] is '/' && source[i - 1] is '[')
            isClosing = true;
        else
            parsedData.Append(source[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleColorOrStyle(ref StringBuilder outText, ref StringBuilder parsedData, ref Stack<ConsoleColor> foregrounds, ref Stack<ConsoleColor> backgrounds, string[] tmp, bool isClosing, bool removeFormatting)
    {
        if (tmp[0].TryGetColor(out var color))
        {
            HandleForegroundColor(ref outText, ref foregrounds, color, isClosing, parsedData, removeFormatting);

            if (tmp.Length is 2 && tmp[1].TryGetColor(out color))
                HandleBackgroundColor(ref outText, ref backgrounds, color, isClosing, parsedData, removeFormatting);
        }
        else if (tmp[0].TryGetStyle(out var style) && style.HasValue)
        {
            HandleStyle(ref outText, style.Value, isClosing, removeFormatting);
        }
        else
        {
            AppendDefaultFormatting(ref outText, parsedData, isClosing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleForegroundColor(ref StringBuilder outText, ref Stack<ConsoleColor> foregrounds, ConsoleColor color, bool isClosing, StringBuilder parsedData, bool removeFormatting)
    {
        if (isClosing)
        {
            if (color == foregrounds.Peek())
            {
                foregrounds.Pop();
                if (!removeFormatting) outText.Append(foregrounds.Peek().AsString());
            }
            else
            {
                if (!removeFormatting) outText.Append($"[/{parsedData}]");
            }
        }
        else
        {
            foregrounds.Push(color);
            if (!removeFormatting) outText.Append(color.AsString());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleBackgroundColor(ref StringBuilder outText, ref Stack<ConsoleColor> backgrounds, ConsoleColor color, bool isClosing, StringBuilder parsedData, bool removeFormatting)
    {
        if (isClosing)
        {
            if (color == backgrounds.Peek())
            {
                backgrounds.Pop();
                if (!removeFormatting) outText.Append(backgrounds.Peek().AsString(true));
            }
            else
            {
                if (!removeFormatting) outText.Append($"[/{parsedData}]");
            }
        }
        else
        {
            backgrounds.Push(color);
            if (!removeFormatting) outText.Append(color.AsString(true));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleStyle(ref StringBuilder outText, ConsoleStyle style, bool isClosing, bool removeFormatting)
    {
        if (isClosing)
        {
            if (!removeFormatting) outText.Append(style.AsString(true));
        }
        else
        {
            if (!removeFormatting) outText.Append(style.AsString());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendDefaultFormatting(ref StringBuilder outText, StringBuilder parsedData, bool isClosing)
    {
        if (parsedData.Length > 0) outText.Append($"[{(isClosing ? '/' : null)}{parsedData}]");
    }

    public static string? Format(string? source, bool removeFormatting)
    {
        if (string.IsNullOrEmpty(source)) return source;

        var outText = ObjectPool<StringBuilder>.Shared.Rent();
        var parsedData = ObjectPool<StringBuilder>.Shared.Rent();

        var foregrounds = ObjectPool<Stack<ConsoleColor>>.Shared.Rent();
        var backgrounds = ObjectPool<Stack<ConsoleColor>>.Shared.Rent();

        foregrounds.Push(Console.ForegroundColor);
        backgrounds.Push(Console.BackgroundColor);

        var isParsing = false;
        var isClosing = false;

        for (var i = 0; i < source.Length; ++i)
        {
            if (source[i] is '[')
                HandleOpeningBracket(ref isParsing, ref outText, ref parsedData, removeFormatting);
            else if (source[i] is ']' && isParsing)
                HandleClosingBracket(ref isParsing, ref isClosing, ref outText, ref parsedData, ref foregrounds, ref backgrounds, removeFormatting);
            else if (isParsing)
                HandleParsingCharacter(ref isClosing, ref parsedData, source, i);
            else
                outText.Append(source[i]);
        }

        var outputText = removeFormatting ? outText.ToString() : $"\x1b[0m{outText}\x1b[0m";

        ObjectPool<Stack<ConsoleColor>>.Shared.Return(backgrounds, x => x.Clear());
        ObjectPool<Stack<ConsoleColor>>.Shared.Return(foregrounds, x => x.Clear());

        ObjectPool<StringBuilder>.Shared.Return(parsedData, x => x.Clear());
        ObjectPool<StringBuilder>.Shared.Return(outText, x => x.Clear());

        return outputText;
    }

    public static string? Format(string? source) => Format(source, default);
}