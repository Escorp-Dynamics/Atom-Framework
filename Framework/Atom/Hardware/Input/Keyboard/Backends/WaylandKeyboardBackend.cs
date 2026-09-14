using System.Runtime.Versioning;

using Atom.Display.Wayland;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Клавиатура на собственном композиторе Wayland.
/// </summary>
/// <remarks>
/// ★ Заменяет XTEST. Протокол передаёт клавишу кодом ядра, а раскладку применяет сам клиент по
/// таблице XKB, которую композитор ему отдал — символ здесь не участвует.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandKeyboardBackend(WaylandInput input) : IVirtualKeyboardBackend
{
    // Явное поле: анализатор не видит захват параметра основного конструктора во вложенных методах.
    private readonly WaylandInput input = input;

    /// <inheritdoc/>
    public string DeviceIdentifier => "wayland-keyboard";

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken)
    {
        // Клавиатурный фокус нужен до первого нажатия: без него клиент отбрасывает события.
        input.SetKeyboardFocus(hasFocus: true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void KeyDown(ConsoleKey key) => SendKey(key, pressed: true);

    /// <inheritdoc/>
    public void KeyUp(ConsoleKey key) => SendKey(key, pressed: false);

    /// <inheritdoc/>
    public void ModifierDown(ConsoleModifiers modifier) => SendModifier(modifier, pressed: true);

    /// <inheritdoc/>
    public void ModifierUp(ConsoleModifiers modifier) => SendModifier(modifier, pressed: false);

    private void SendKey(ConsoleKey key, bool pressed)
    {
        if (WaylandKeyCodes.FromConsoleKey(key) is { } code)
            input.SendKey(code, pressed);
    }

    private void SendModifier(ConsoleModifiers modifier, bool pressed)
    {
        if (modifier.HasFlag(ConsoleModifiers.Shift))
            input.SendKey(WaylandKeyCodes.LeftShift, pressed);

        if (modifier.HasFlag(ConsoleModifiers.Control))
            input.SendKey(WaylandKeyCodes.LeftControl, pressed);

        if (modifier.HasFlag(ConsoleModifiers.Alt))
            input.SendKey(WaylandKeyCodes.LeftAlt, pressed);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
