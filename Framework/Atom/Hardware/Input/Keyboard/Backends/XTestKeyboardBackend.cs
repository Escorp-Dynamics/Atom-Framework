#pragma warning disable MA0051

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной клавиатуры для X11 через расширение XTEST.
/// Инжектит события клавиатуры напрямую в указанный X-сервер,
/// не затрагивая другие дисплеи или физическую клавиатуру.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class XTestKeyboardBackend : IVirtualKeyboardBackend
{
    private nint display;
    private int screenNumber;
    private bool isDisposed;
    private readonly Dictionary<nuint, uint> dynamicKeycodeCache = [];
    private string LastFocusStrategy { get; set; } = "<none>";
    private nint LastPointerChildWindow { get; set; }
    private nint LastExistingFocusWindow { get; set; }
    private nint LastAssignedFocusWindow { get; set; }

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <summary>
    /// Строка дисплея X11 (например, <c>:99</c>).
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new VirtualKeyboardException("DisplayName не задан для XTEST-клавиатуры.");

        display = XOpenDisplay(DisplayName);
        if (display == nint.Zero)
        {
            throw new VirtualKeyboardException(
                "Не удалось подключиться к X-серверу " + DisplayName + ". " +
                "Убедитесь что целевой X11 display доступен и DISPLAY задан корректно.");
        }

        var hasXTest = XTestQueryExtension(
            display, out _, out _, out _, out _);

        if (!hasXTest)
        {
            _ = XCloseDisplay(display);
            display = nint.Zero;
            throw new VirtualKeyboardException(
                "X-сервер " + DisplayName + " не поддерживает расширение XTEST.");
        }

        screenNumber = XDefaultScreen(display);
        DeviceIdentifier = "xtest:" + DisplayName;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Задаёт строку дисплея для подключения.
    /// Должен быть вызван перед <see cref="InitializeAsync"/>.
    /// </summary>
    internal void SetDisplayName(string displayName) =>
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("DisplayName не может быть пустым.", nameof(displayName))
            : displayName;

    /// <inheritdoc/>
    public void KeyDown(ConsoleKey key)
    {
        ThrowIfUnavailable();
        EnsurePointerFocused();
        var keycode = ConsoleKeyToKeycode(key);
        _ = XTestFakeKeyEvent(display, keycode, isPress: 1, delay: 0);
        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void KeyUp(ConsoleKey key)
    {
        ThrowIfUnavailable();
        var keycode = ConsoleKeyToKeycode(key);
        _ = XTestFakeKeyEvent(display, keycode, isPress: 0, delay: 0);
        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void ModifierDown(ConsoleModifiers modifier)
    {
        ThrowIfUnavailable();
        SendModifier(modifier, isPress: 1);
    }

    /// <inheritdoc/>
    public void ModifierUp(ConsoleModifiers modifier)
    {
        ThrowIfUnavailable();
        SendModifier(modifier, isPress: 0);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true))
            return ValueTask.CompletedTask;

        if (display != nint.Zero)
        {
            _ = XCloseDisplay(display);
            display = nint.Zero;
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();

        if (display == nint.Zero)
            throw new VirtualKeyboardException("XTEST-клавиатура не инициализирована или уже отключена от X-сервера.");
    }

    private void EnsurePointerFocused()
    {
        var rootWindow = XRootWindow(display, screenNumber);
        var focusWindow = rootWindow;
        var focusStrategy = "root";
        var pointerChildWindow = nint.Zero;
        var existingFocusWindow = nint.Zero;

        if (XQueryPointer(
            display,
            rootWindow,
            out _,
            out var childWindow,
            out _,
            out _,
            out _,
            out _,
            out _)
            && childWindow != nint.Zero)
        {
            pointerChildWindow = childWindow;
            focusWindow = childWindow;
            focusStrategy = "pointer";
        }
        else if (TryGetFocusedWindow(rootWindow, out existingFocusWindow))
        {
            focusWindow = existingFocusWindow;
            focusStrategy = "existing";
        }

        LastPointerChildWindow = pointerChildWindow;
        LastExistingFocusWindow = existingFocusWindow;
        LastAssignedFocusWindow = focusWindow;
        LastFocusStrategy = focusStrategy;

        _ = XSetInputFocus(display, focusWindow, RevertToParent, CurrentTime);
        _ = XFlush(display);
    }

    private bool TryGetFocusedWindow(nint rootWindow, out nint focusedWindow)
    {
        focusedWindow = nint.Zero;

        if (XGetInputFocus(display, out var existingFocusWindow, out _) == 0)
            return false;

        if (existingFocusWindow == nint.Zero
            || existingFocusWindow == PointerRootWindow
            || existingFocusWindow == rootWindow)
        {
            return false;
        }

        focusedWindow = existingFocusWindow;
        return true;
    }

    private uint ConsoleKeyToKeycode(ConsoleKey key)
    {
        var keysym = MapConsoleKeyToKeysym(key);

        if (dynamicKeycodeCache.TryGetValue(keysym, out var cachedKeycode))
            return cachedKeycode;

        var keycode = XKeysymToKeycode(display, keysym);
        if (keycode == 0)
        {
            keycode = EnsureKeycodeForKeysym(keysym);
        }

        if (keycode == 0)
        {
            throw new VirtualKeyboardException(
                "X-сервер " + DisplayName + " не смог сопоставить keycode для " + key + ".");
        }

        return keycode;
    }

    private unsafe uint EnsureKeycodeForKeysym(nuint keysym)
    {
        if (dynamicKeycodeCache.TryGetValue(keysym, out var cachedKeycode))
            return cachedKeycode;

        if (!XDisplayKeycodes(display, out var minKeycode, out var maxKeycode))
            return 0;

        var keycodeCount = maxKeycode - minKeycode + 1;
        if (keycodeCount <= 0)
            return 0;

        var mapping = XGetKeyboardMapping(display, (byte)minKeycode, keycodeCount, out var keysymsPerKeycode);
        if (mapping == null || keysymsPerKeycode <= 0)
            return 0;

        try
        {
            for (var index = 0; index < keycodeCount; index++)
            {
                if (!IsFreeKeycodeSlot(mapping, index, keysymsPerKeycode))
                    continue;

                var keycode = (uint)(minKeycode + index);
                var replacement = new nuint[keysymsPerKeycode];
                replacement[0] = keysym;

                fixed (nuint* replacementPtr = replacement)
                {
                    _ = XChangeKeyboardMapping(display, (int)keycode, keysymsPerKeycode, replacementPtr, 1);
                }

                _ = XSync(display, discard: 0);

                var resolvedKeycode = XKeysymToKeycode(display, keysym);
                if (resolvedKeycode == 0)
                    resolvedKeycode = keycode;

                dynamicKeycodeCache[keysym] = resolvedKeycode;
                return resolvedKeycode;
            }
        }
        finally
        {
            _ = XFree(mapping);
        }

        return 0;
    }

    private static unsafe bool IsFreeKeycodeSlot(nuint* mapping, int index, int keysymsPerKeycode)
    {
        var offset = index * keysymsPerKeycode;
        for (var slot = 0; slot < keysymsPerKeycode; slot++)
        {
            if (mapping[offset + slot] != 0)
                return false;
        }

        return true;
    }

    private void SendModifier(ConsoleModifiers modifier, int isPress)
    {
        if (modifier.HasFlag(ConsoleModifiers.Control))
        {
            var kc = XKeysymToKeycode(display, XK_CONTROL_L);
            if (kc != 0)
                _ = XTestFakeKeyEvent(display, kc, isPress, delay: 0);
        }

        if (modifier.HasFlag(ConsoleModifiers.Alt))
        {
            var kc = XKeysymToKeycode(display, XK_ALT_L);
            if (kc != 0)
                _ = XTestFakeKeyEvent(display, kc, isPress, delay: 0);
        }

        if (modifier.HasFlag(ConsoleModifiers.Shift))
        {
            var kc = XKeysymToKeycode(display, XK_SHIFT_L);
            if (kc != 0)
                _ = XTestFakeKeyEvent(display, kc, isPress, delay: 0);
        }

        _ = XFlush(display);
    }

    private static nuint MapConsoleKeyToKeysym(ConsoleKey key) => key switch
    {
        ConsoleKey.Backspace => 0xFF08,
        ConsoleKey.Tab => 0xFF09,
        ConsoleKey.Enter => 0xFF0D,
        ConsoleKey.Pause => 0xFF13,
        ConsoleKey.Escape => 0xFF1B,
        ConsoleKey.Spacebar => 0x0020,
        ConsoleKey.OemPlus => 0x003D,
        ConsoleKey.OemComma => 0x002C,
        ConsoleKey.OemMinus => 0x002D,
        ConsoleKey.OemPeriod => 0x002E,
        ConsoleKey.Oem1 => 0x003B,
        ConsoleKey.Oem2 => 0x002F,
        ConsoleKey.Oem3 => 0x0060,
        ConsoleKey.Oem4 => 0x005B,
        ConsoleKey.Oem5 => 0x005C,
        ConsoleKey.Oem6 => 0x005D,
        ConsoleKey.Oem7 => 0x0027,
        ConsoleKey.PageUp => 0xFF55,
        ConsoleKey.PageDown => 0xFF56,
        ConsoleKey.End => 0xFF57,
        ConsoleKey.Home => 0xFF50,
        ConsoleKey.LeftArrow => 0xFF51,
        ConsoleKey.UpArrow => 0xFF52,
        ConsoleKey.RightArrow => 0xFF53,
        ConsoleKey.DownArrow => 0xFF54,
        ConsoleKey.PrintScreen => 0xFF61,
        ConsoleKey.Insert => 0xFF63,
        ConsoleKey.Delete => 0xFFFF,
        ConsoleKey.D0 => 0x0030,
        ConsoleKey.D1 => 0x0031,
        ConsoleKey.D2 => 0x0032,
        ConsoleKey.D3 => 0x0033,
        ConsoleKey.D4 => 0x0034,
        ConsoleKey.D5 => 0x0035,
        ConsoleKey.D6 => 0x0036,
        ConsoleKey.D7 => 0x0037,
        ConsoleKey.D8 => 0x0038,
        ConsoleKey.D9 => 0x0039,
        >= ConsoleKey.A and <= ConsoleKey.Z => (nuint)(0x0061 + (key - ConsoleKey.A)),
        ConsoleKey.LeftWindows => XK_SUPER_L,
        ConsoleKey.RightWindows => XK_SUPER_R,
        ConsoleKey.NumPad0 => 0xFFB0,
        ConsoleKey.NumPad1 => 0xFFB1,
        ConsoleKey.NumPad2 => 0xFFB2,
        ConsoleKey.NumPad3 => 0xFFB3,
        ConsoleKey.NumPad4 => 0xFFB4,
        ConsoleKey.NumPad5 => 0xFFB5,
        ConsoleKey.NumPad6 => 0xFFB6,
        ConsoleKey.NumPad7 => 0xFFB7,
        ConsoleKey.NumPad8 => 0xFFB8,
        ConsoleKey.NumPad9 => 0xFFB9,
        ConsoleKey.Multiply => 0xFFAA,
        ConsoleKey.Add => 0xFFAB,
        ConsoleKey.Subtract => 0xFFAD,
        ConsoleKey.Decimal => 0xFFAE,
        ConsoleKey.Divide => 0xFFAF,
        ConsoleKey.F1 => 0xFFBE,
        ConsoleKey.F2 => 0xFFBF,
        ConsoleKey.F3 => 0xFFC0,
        ConsoleKey.F4 => 0xFFC1,
        ConsoleKey.F5 => 0xFFC2,
        ConsoleKey.F6 => 0xFFC3,
        ConsoleKey.F7 => 0xFFC4,
        ConsoleKey.F8 => 0xFFC5,
        ConsoleKey.F9 => 0xFFC6,
        ConsoleKey.F10 => 0xFFC7,
        ConsoleKey.F11 => 0xFFC8,
        ConsoleKey.F12 => 0xFFC9,
        ConsoleKey.F13 => 0xFFCA,
        ConsoleKey.F14 => 0xFFCB,
        ConsoleKey.F15 => 0xFFCC,
        ConsoleKey.F16 => 0xFFCD,
        ConsoleKey.F17 => 0xFFCE,
        ConsoleKey.F18 => 0xFFCF,
        ConsoleKey.F19 => 0xFFD0,
        ConsoleKey.F20 => 0xFFD1,
        ConsoleKey.F21 => 0xFFD2,
        ConsoleKey.F22 => 0xFFD3,
        ConsoleKey.F23 => 0xFFD4,
        ConsoleKey.F24 => 0xFFD5,
        _ => throw new ArgumentOutOfRangeException(
            nameof(key), key, "Нет маппинга ConsoleKey → X11 keysym для " + key + "."),
    };

    // X11 keysym constants for modifiers.
    private const nuint XK_SHIFT_L = 0xFFE1;
    private const nuint XK_CONTROL_L = 0xFFE3;
    private const nuint XK_ALT_L = 0xFFE9;
    private const nuint XK_SUPER_L = 0xFFEB;
    private const nuint XK_SUPER_R = 0xFFEC;

    #region Native interop

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XFlush(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XRootWindow(nint display, int screenNumber);

    [LibraryImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XSetInputFocus(nint display, nint focus, int revertTo, nuint time);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetInputFocus")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XGetInputFocus(nint display, out nint focusReturn, out int revertToReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XQueryPointer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XQueryPointer(
        nint display,
        nint window,
        out nint rootReturn,
        out nint childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int winXReturn,
        out int winYReturn,
        out uint maskReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XKeysymToKeycode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial uint XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport("libX11.so.6", EntryPoint = "XDisplayKeycodes")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XDisplayKeycodes(
        nint display,
        out int minKeycodesReturn,
        out int maxKeycodesReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetKeyboardMapping")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nuint* XGetKeyboardMapping(
        nint display,
        byte firstKeycode,
        int keycodeCount,
        out int keysymsPerKeycodeReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XChangeKeyboardMapping")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int XChangeKeyboardMapping(
        nint display,
        int firstKeycode,
        int keysymsPerKeycode,
        nuint* keysyms,
        int keycodeCount);

    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XSync(nint display, int discard);

    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int XFree(void* data);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestQueryExtension")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XTestQueryExtension(
        nint display,
        out int eventBasep,
        out int errorBasep,
        out int majorVersionp,
        out int minorVersionp);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeKeyEvent")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XTestFakeKeyEvent(
        nint display, uint keycode, int isPress, nuint delay);

    private const int RevertToParent = 2;
    private const nuint CurrentTime = 0;
    private static readonly nint PointerRootWindow = 1;

    #endregion
}