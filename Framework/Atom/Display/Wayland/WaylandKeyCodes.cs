namespace Atom.Display.Wayland;

/// <summary>
/// Перевод клавиш в коды ядра Linux.
/// </summary>
/// <remarks>
/// ★ Протокол передаёт клавишу кодом из <c lang="text">linux/input-event-codes.h</c> — тем же, что
/// приходит от настоящей клавиатуры. Раскладку применяет сам клиент по переданной ему таблице XKB,
/// поэтому здесь нужны именно физические коды, а не символы.
/// </remarks>
public static class WaylandKeyCodes
{
    /// <summary>
    /// Возвращает код ядра для клавиши.
    /// </summary>
    /// <param name="key">Клавиша в терминах консоли.</param>
    /// <returns>Код ядра или <see langword="null"/>, если соответствия нет.</returns>
    public static uint? FromConsoleKey(ConsoleKey key)
        => Letters(key) ?? Digits(key) ?? Functional(key) ?? Controls(key) ?? Punctuation(key);

    private static uint? Letters(ConsoleKey key) => key switch
    {
        ConsoleKey.A => 30,
        ConsoleKey.B => 48,
        ConsoleKey.C => 46,
        ConsoleKey.D => 32,
        ConsoleKey.E => 18,
        ConsoleKey.F => 33,
        ConsoleKey.G => 34,
        ConsoleKey.H => 35,
        ConsoleKey.I => 23,
        ConsoleKey.J => 36,
        ConsoleKey.K => 37,
        ConsoleKey.L => 38,
        ConsoleKey.M => 50,
        ConsoleKey.N => 49,
        ConsoleKey.O => 24,
        ConsoleKey.P => 25,
        ConsoleKey.Q => 16,
        ConsoleKey.R => 19,
        ConsoleKey.S => 31,
        ConsoleKey.T => 20,
        ConsoleKey.U => 22,
        ConsoleKey.V => 47,
        ConsoleKey.W => 17,
        ConsoleKey.X => 45,
        ConsoleKey.Y => 21,
        ConsoleKey.Z => 44,
        _ => null,
    };

    private static uint? Digits(ConsoleKey key) => key switch
    {
        ConsoleKey.D0 => 11,
        ConsoleKey.D1 => 2,
        ConsoleKey.D2 => 3,
        ConsoleKey.D3 => 4,
        ConsoleKey.D4 => 5,
        ConsoleKey.D5 => 6,
        ConsoleKey.D6 => 7,
        ConsoleKey.D7 => 8,
        ConsoleKey.D8 => 9,
        ConsoleKey.D9 => 10,

        ConsoleKey.NumPad0 => 82,
        ConsoleKey.NumPad1 => 79,
        ConsoleKey.NumPad2 => 80,
        ConsoleKey.NumPad3 => 81,
        ConsoleKey.NumPad4 => 75,
        ConsoleKey.NumPad5 => 76,
        ConsoleKey.NumPad6 => 77,
        ConsoleKey.NumPad7 => 71,
        ConsoleKey.NumPad8 => 72,
        ConsoleKey.NumPad9 => 73,
        _ => null,
    };

    private static uint? Functional(ConsoleKey key) => key switch
    {
        ConsoleKey.F1 => 59,
        ConsoleKey.F2 => 60,
        ConsoleKey.F3 => 61,
        ConsoleKey.F4 => 62,
        ConsoleKey.F5 => 63,
        ConsoleKey.F6 => 64,
        ConsoleKey.F7 => 65,
        ConsoleKey.F8 => 66,
        ConsoleKey.F9 => 67,
        ConsoleKey.F10 => 68,
        ConsoleKey.F11 => 87,
        ConsoleKey.F12 => 88,
        _ => null,
    };

    private static uint? Controls(ConsoleKey key) => key switch
    {
        ConsoleKey.Escape => 1,
        ConsoleKey.Backspace => 14,
        ConsoleKey.Tab => 15,
        ConsoleKey.Enter => 28,
        ConsoleKey.Spacebar => 57,
        ConsoleKey.Delete => 111,
        ConsoleKey.Insert => 110,
        ConsoleKey.Home => 102,
        ConsoleKey.End => 107,
        ConsoleKey.PageUp => 104,
        ConsoleKey.PageDown => 109,
        ConsoleKey.UpArrow => 103,
        ConsoleKey.DownArrow => 108,
        ConsoleKey.LeftArrow => 105,
        ConsoleKey.RightArrow => 106,
        _ => null,
    };

    private static uint? Punctuation(ConsoleKey key) => key switch
    {
        ConsoleKey.OemMinus => 12,
        ConsoleKey.OemPlus => 13,
        ConsoleKey.Oem1 => 39,
        ConsoleKey.Oem2 => 53,
        ConsoleKey.Oem3 => 41,
        ConsoleKey.Oem4 => 26,
        ConsoleKey.Oem5 => 43,
        ConsoleKey.Oem6 => 27,
        ConsoleKey.Oem7 => 40,
        ConsoleKey.OemComma => 51,
        ConsoleKey.OemPeriod => 52,

        _ => null,
    };

    /// <summary>Код левой клавиши сдвига.</summary>
    public const uint LeftShift = 42;

    /// <summary>Код левой управляющей клавиши.</summary>
    public const uint LeftControl = 29;

    /// <summary>Код левой клавиши alt.</summary>
    public const uint LeftAlt = 56;

    /// <summary>Код левой системной клавиши.</summary>
    public const uint LeftMeta = 125;
}
