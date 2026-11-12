using System.Globalization;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описание клавиши для <c lang="text">Input.dispatchKeyEvent</c>.
/// </summary>
/// <param name="Key">Значение <c lang="text">KeyboardEvent.key</c>.</param>
/// <param name="Code">Значение <c lang="text">KeyboardEvent.code</c>.</param>
/// <param name="VirtualKeyCode">Код виртуальной клавиши Windows (<c lang="text">keyCode</c>).</param>
/// <param name="Text">Вводимый текст либо <see langword="null"/> для управляющей клавиши.</param>
internal readonly record struct CdpKeyDefinition(string Key, string Code, int VirtualKeyCode, string? Text);

/// <summary>
/// Перевод клавиш и символов в события клавиатуры протокола отладки.
/// </summary>
internal static class CdpKeyMap
{
    /// <summary>Битовая маска модификаторов CDP: Alt=1, Ctrl=2, Meta=4, Shift=8.</summary>
    public static int ToModifierMask(ConsoleModifiers modifiers)
    {
        var mask = 0;
        if (modifiers.HasFlag(ConsoleModifiers.Alt))
            mask |= 1;

        if (modifiers.HasFlag(ConsoleModifiers.Control))
            mask |= 2;

        if (modifiers.HasFlag(ConsoleModifiers.Shift))
            mask |= 8;

        return mask;
    }

    // Управляющие и знаковые клавиши: key/code/keyCode и вводимый текст без Shift и с ним.
    private static readonly Dictionary<ConsoleKey, (string Key, string Code, int VirtualKeyCode, string? Text, string? ShiftedText)> NamedKeys = new()
    {
        [ConsoleKey.Enter] = ("Enter", "Enter", 13, "\r", "\r"),
        [ConsoleKey.Tab] = ("Tab", "Tab", 9, null, null),
        [ConsoleKey.Backspace] = ("Backspace", "Backspace", 8, null, null),
        [ConsoleKey.Escape] = ("Escape", "Escape", 27, null, null),
        [ConsoleKey.Spacebar] = (" ", "Space", 32, " ", " "),
        [ConsoleKey.Delete] = ("Delete", "Delete", 46, null, null),
        [ConsoleKey.Insert] = ("Insert", "Insert", 45, null, null),
        [ConsoleKey.Home] = ("Home", "Home", 36, null, null),
        [ConsoleKey.End] = ("End", "End", 35, null, null),
        [ConsoleKey.PageUp] = ("PageUp", "PageUp", 33, null, null),
        [ConsoleKey.PageDown] = ("PageDown", "PageDown", 34, null, null),
        [ConsoleKey.LeftArrow] = ("ArrowLeft", "ArrowLeft", 37, null, null),
        [ConsoleKey.UpArrow] = ("ArrowUp", "ArrowUp", 38, null, null),
        [ConsoleKey.RightArrow] = ("ArrowRight", "ArrowRight", 39, null, null),
        [ConsoleKey.DownArrow] = ("ArrowDown", "ArrowDown", 40, null, null),
        [ConsoleKey.Multiply] = ("*", "NumpadMultiply", 106, "*", "*"),
        [ConsoleKey.Add] = ("+", "NumpadAdd", 107, "+", "+"),
        [ConsoleKey.Subtract] = ("-", "NumpadSubtract", 109, "-", "-"),
        [ConsoleKey.Decimal] = (".", "NumpadDecimal", 110, ".", "."),
        [ConsoleKey.Divide] = ("/", "NumpadDivide", 111, "/", "/"),
        [ConsoleKey.OemPlus] = ("=", "Equal", 187, "=", "+"),
        [ConsoleKey.OemComma] = (",", "Comma", 188, ",", "<"),
        [ConsoleKey.OemMinus] = ("-", "Minus", 189, "-", "_"),
        [ConsoleKey.OemPeriod] = (".", "Period", 190, ".", ">"),
    };

    /// <summary>Описание клавиши <paramref name="key"/> с учётом модификаторов.</summary>
    public static CdpKeyDefinition Resolve(ConsoleKey key, ConsoleModifiers modifiers)
    {
        var shift = modifiers.HasFlag(ConsoleModifiers.Shift);
        var printable = (modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == ConsoleModifiers.None;

        if (TryResolveRange(key, shift, out var definition))
            return printable ? definition : definition with { Text = null };

        if (NamedKeys.TryGetValue(key, out var named))
        {
            var text = shift ? named.ShiftedText : named.Text;
            // Знаковая клавиша с Shift сообщает в key другой символ (= → +), остальные — своё имя.
            var keyName = shift && named.ShiftedText is { } shifted && !string.Equals(shifted, named.Text, StringComparison.Ordinal) ? shifted : named.Key;
            return new(keyName, named.Code, named.VirtualKeyCode, printable ? text : null);
        }

        var name = key.ToString();
        return new(name, name, (int)key, Text: null);
    }

    private static bool TryResolveRange(ConsoleKey key, bool shift, out CdpKeyDefinition definition)
    {
        if (key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            var letter = (char)('a' + (key - ConsoleKey.A));
            var text = (shift ? char.ToUpperInvariant(letter) : letter).ToString();
            definition = new(text, string.Concat("Key", char.ToUpperInvariant(letter).ToString()), (int)key, text);
            return true;
        }

        if (key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
        {
            var digit = (key - ConsoleKey.D0).ToString(CultureInfo.InvariantCulture);
            definition = new(digit, string.Concat("Digit", digit), (int)key, digit);
            return true;
        }

        if (key is >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9)
        {
            var digit = (key - ConsoleKey.NumPad0).ToString(CultureInfo.InvariantCulture);
            definition = new(digit, string.Concat("Numpad", digit), (int)key, digit);
            return true;
        }

        if (key is >= ConsoleKey.F1 and <= ConsoleKey.F24)
        {
            var name = string.Concat("F", (key - ConsoleKey.F1 + 1).ToString(CultureInfo.InvariantCulture));
            definition = new(name, name, (int)key, Text: null);
            return true;
        }

        definition = default;
        return false;
    }

    /// <summary>
    /// Описание клавиши, которой набирается символ <paramref name="character"/>.
    /// </summary>
    /// <remarks>
    /// Символы вне латинской раскладки (кириллица, эмодзи) получают пустой <c lang="text">code</c>
    /// и нулевой код клавиши — так же браузер сообщает ввод через IME, а текст доходит до поля целиком.
    /// </remarks>
    public static (CdpKeyDefinition Definition, int Modifiers) ResolveCharacter(string character)
    {
        ArgumentException.ThrowIfNullOrEmpty(character);

        if (character is "\n" or "\r")
            return (Resolve(ConsoleKey.Enter, default), 0);

        if (string.Equals(character, "\t", StringComparison.Ordinal))
            return (Resolve(ConsoleKey.Tab, default), 0);

        if (character.Length == 1)
        {
            var symbol = character[0];
            if (symbol is >= 'a' and <= 'z')
                return (Resolve(ConsoleKey.A + (symbol - 'a'), default), 0);

            if (symbol is >= 'A' and <= 'Z')
                return (Resolve(ConsoleKey.A + (symbol - 'A'), ConsoleModifiers.Shift), 8);

            if (symbol is >= '0' and <= '9')
                return (Resolve(ConsoleKey.D0 + (symbol - '0'), default), 0);

            if (symbol == ' ')
                return (Resolve(ConsoleKey.Spacebar, default), 0);
        }

        return (new CdpKeyDefinition(character, string.Empty, 0, character), 0);
    }
}
