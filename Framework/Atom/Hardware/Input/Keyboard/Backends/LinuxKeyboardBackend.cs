#pragma warning disable CA1308, CA1823, MA0051, S1144

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной клавиатуры для Linux через /dev/uinput.
/// Создаёт виртуальное устройство клавиатуры на уровне ядра.
/// Требует доступ к /dev/uinput (группа input или udev-правило).
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxKeyboardBackend : IVirtualKeyboardBackend
{
    private int fd = -1;
    private bool isDisposed;

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        fd = Open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (fd < 0)
        {
            throw new VirtualKeyboardException(
                "/dev/uinput недоступен. Убедитесь что:" + Environment.NewLine +
                "  1. Модуль ядра загружен: sudo modprobe uinput" + Environment.NewLine +
                "  2. Пользователь в группе input: sudo usermod -aG input $USER" + Environment.NewLine +
                "  3. udev-правило: KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\"");
        }

        try
        {
            ConfigureDevice();
            SetupDevice(settings);
            CreateDevice();
        }
        catch
        {
            Close(fd);
            fd = -1;
            throw;
        }

        DeviceIdentifier = "uinput:" + settings.Name;

        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void KeyDown(ConsoleKey key)
    {
        ThrowIfDisposed();
        var scanCode = MapConsoleKey(key);
        WriteEvent(EV_KEY, scanCode, 1);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void KeyUp(ConsoleKey key)
    {
        ThrowIfDisposed();
        var scanCode = MapConsoleKey(key);
        WriteEvent(EV_KEY, scanCode, 0);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void ModifierDown(ConsoleModifiers modifier)
    {
        ThrowIfDisposed();
        WriteModifierEvents(modifier, 1);
    }

    /// <inheritdoc/>
    public void ModifierUp(ConsoleModifiers modifier)
    {
        ThrowIfDisposed();
        WriteModifierEvents(modifier, 0);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true))
        {
            return ValueTask.CompletedTask;
        }

        if (fd >= 0)
        {
            _ = Ioctl(fd, UI_DEV_DESTROY);
            _ = Close(fd);
            fd = -1;
        }

        return ValueTask.CompletedTask;
    }

    private void ConfigureDevice()
    {
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_KEY, "UI_SET_EVBIT(EV_KEY)");
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_SYN, "UI_SET_EVBIT(EV_SYN)");

        // Регистрируем все клавиши, которые поддерживает маппинг.
        for (ushort code = 1; code <= KEY_MAX; code++)
        {
            IoctlOrThrow(fd, UI_SET_KEYBIT, code, "UI_SET_KEYBIT");
        }
    }

    private unsafe void SetupDevice(VirtualKeyboardSettings settings)
    {
        var setup = new UinputSetup();

        setup.Id.BusType = settings.UsbVendorId.HasValue ? BUS_USB : BUS_VIRTUAL;
        setup.Id.Vendor = (ushort)(settings.UsbVendorId ?? 0);
        setup.Id.Product = (ushort)(settings.UsbProductId ?? 0);
        setup.Id.Version = 1;

        var nameBytes = Encoding.UTF8.GetBytes(settings.Name);
        var copyLength = Math.Min(nameBytes.Length, UINPUT_MAX_NAME_SIZE - 1);
        Span<byte> nameSpan = setup.Name;
        nameBytes.AsSpan(0, copyLength).CopyTo(nameSpan);
        nameSpan[copyLength] = 0;

        if (Ioctl(fd, UI_DEV_SETUP, &setup) < 0)
        {
            throw new VirtualKeyboardException("ioctl UI_DEV_SETUP не удался.");
        }
    }

    private void CreateDevice()
    {
        if (Ioctl(fd, UI_DEV_CREATE) < 0)
        {
            throw new VirtualKeyboardException("ioctl UI_DEV_CREATE не удался.");
        }
    }

    private unsafe void WriteEvent(ushort type, ushort code, int value)
    {
        var ev = new InputEvent
        {
            Type = type,
            Code = code,
            Value = value,
        };

        var written = Write(fd, &ev, (nuint)sizeof(InputEvent));
        if (written < 0)
        {
            throw new VirtualKeyboardException(
                "Не удалось записать событие в /dev/uinput.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

    private void WriteModifierEvents(ConsoleModifiers modifier, int value)
    {
        if (modifier.HasFlag(ConsoleModifiers.Control))
        {
            WriteEvent(EV_KEY, KEY_LEFTCTRL, value);
        }

        if (modifier.HasFlag(ConsoleModifiers.Alt))
        {
            WriteEvent(EV_KEY, KEY_LEFTALT, value);
        }

        if (modifier.HasFlag(ConsoleModifiers.Shift))
        {
            WriteEvent(EV_KEY, KEY_LEFTSHIFT, value);
        }

        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    private static void IoctlOrThrow(int deviceFd, nuint request, int value, string description)
    {
        if (Ioctl(deviceFd, request, value) < 0)
        {
            throw new VirtualKeyboardException("ioctl " + description + " не удался.");
        }
    }

    internal static ushort MapConsoleKey(ConsoleKey key) => key switch
    {
        ConsoleKey.Backspace => KEY_BACKSPACE,
        ConsoleKey.Tab => KEY_TAB,
        ConsoleKey.Enter => KEY_ENTER,
        ConsoleKey.Pause => KEY_PAUSE,
        ConsoleKey.Escape => KEY_ESC,
        ConsoleKey.Spacebar => KEY_SPACE,
        ConsoleKey.PageUp => KEY_PAGEUP,
        ConsoleKey.PageDown => KEY_PAGEDOWN,
        ConsoleKey.End => KEY_END,
        ConsoleKey.Home => KEY_HOME,
        ConsoleKey.LeftArrow => KEY_LEFT,
        ConsoleKey.UpArrow => KEY_UP,
        ConsoleKey.RightArrow => KEY_RIGHT,
        ConsoleKey.DownArrow => KEY_DOWN,
        ConsoleKey.PrintScreen => KEY_SYSRQ,
        ConsoleKey.Insert => KEY_INSERT,
        ConsoleKey.Delete => KEY_DELETE,
        ConsoleKey.D0 => KEY_0,
        ConsoleKey.D1 => KEY_1,
        ConsoleKey.D2 => KEY_2,
        ConsoleKey.D3 => KEY_3,
        ConsoleKey.D4 => KEY_4,
        ConsoleKey.D5 => KEY_5,
        ConsoleKey.D6 => KEY_6,
        ConsoleKey.D7 => KEY_7,
        ConsoleKey.D8 => KEY_8,
        ConsoleKey.D9 => KEY_9,
        ConsoleKey.A => KEY_A,
        ConsoleKey.B => KEY_B,
        ConsoleKey.C => KEY_C,
        ConsoleKey.D => KEY_D,
        ConsoleKey.E => KEY_E,
        ConsoleKey.F => KEY_F,
        ConsoleKey.G => KEY_G,
        ConsoleKey.H => KEY_H,
        ConsoleKey.I => KEY_I,
        ConsoleKey.J => KEY_J,
        ConsoleKey.K => KEY_K,
        ConsoleKey.L => KEY_L,
        ConsoleKey.M => KEY_M,
        ConsoleKey.N => KEY_N,
        ConsoleKey.O => KEY_O,
        ConsoleKey.P => KEY_P,
        ConsoleKey.Q => KEY_Q,
        ConsoleKey.R => KEY_R,
        ConsoleKey.S => KEY_S,
        ConsoleKey.T => KEY_T,
        ConsoleKey.U => KEY_U,
        ConsoleKey.V => KEY_V,
        ConsoleKey.W => KEY_W,
        ConsoleKey.X => KEY_X,
        ConsoleKey.Y => KEY_Y,
        ConsoleKey.Z => KEY_Z,
        ConsoleKey.LeftWindows => KEY_LEFTMETA,
        ConsoleKey.RightWindows => KEY_RIGHTMETA,
        ConsoleKey.NumPad0 => KEY_KP0,
        ConsoleKey.NumPad1 => KEY_KP1,
        ConsoleKey.NumPad2 => KEY_KP2,
        ConsoleKey.NumPad3 => KEY_KP3,
        ConsoleKey.NumPad4 => KEY_KP4,
        ConsoleKey.NumPad5 => KEY_KP5,
        ConsoleKey.NumPad6 => KEY_KP6,
        ConsoleKey.NumPad7 => KEY_KP7,
        ConsoleKey.NumPad8 => KEY_KP8,
        ConsoleKey.NumPad9 => KEY_KP9,
        ConsoleKey.Multiply => KEY_KPASTERISK,
        ConsoleKey.Add => KEY_KPPLUS,
        ConsoleKey.Subtract => KEY_KPMINUS,
        ConsoleKey.Decimal => KEY_KPDOT,
        ConsoleKey.Divide => KEY_KPSLASH,
        ConsoleKey.F1 => KEY_F1,
        ConsoleKey.F2 => KEY_F2,
        ConsoleKey.F3 => KEY_F3,
        ConsoleKey.F4 => KEY_F4,
        ConsoleKey.F5 => KEY_F5,
        ConsoleKey.F6 => KEY_F6,
        ConsoleKey.F7 => KEY_F7,
        ConsoleKey.F8 => KEY_F8,
        ConsoleKey.F9 => KEY_F9,
        ConsoleKey.F10 => KEY_F10,
        ConsoleKey.F11 => KEY_F11,
        ConsoleKey.F12 => KEY_F12,
        ConsoleKey.F13 => KEY_F13,
        ConsoleKey.F14 => KEY_F14,
        ConsoleKey.F15 => KEY_F15,
        ConsoleKey.F16 => KEY_F16,
        ConsoleKey.F17 => KEY_F17,
        ConsoleKey.F18 => KEY_F18,
        ConsoleKey.F19 => KEY_F19,
        ConsoleKey.F20 => KEY_F20,
        ConsoleKey.F21 => KEY_F21,
        ConsoleKey.F22 => KEY_F22,
        ConsoleKey.F23 => KEY_F23,
        ConsoleKey.F24 => KEY_F24,
        ConsoleKey.OemComma => KEY_COMMA,
        ConsoleKey.OemMinus => KEY_MINUS,
        ConsoleKey.OemPeriod => KEY_DOT,
        ConsoleKey.Oem1 => KEY_SEMICOLON,
        ConsoleKey.Oem2 => KEY_SLASH,
        ConsoleKey.Oem3 => KEY_GRAVE,
        ConsoleKey.Oem4 => KEY_LEFTBRACE,
        ConsoleKey.Oem5 => KEY_BACKSLASH,
        ConsoleKey.Oem6 => KEY_RIGHTBRACE,
        ConsoleKey.Oem7 => KEY_APOSTROPHE,
        ConsoleKey.OemPlus => KEY_EQUAL,
        _ => throw new ArgumentOutOfRangeException(
            nameof(key), key, "Нет маппинга ConsoleKey → Linux scancode для " + key + "."),
    };

    internal static ushort MapModifier(ConsoleModifiers modifier) => modifier switch
    {
        ConsoleModifiers.Shift => KEY_LEFTSHIFT,
        ConsoleModifiers.Control => KEY_LEFTCTRL,
        ConsoleModifiers.Alt => KEY_LEFTALT,
        _ => throw new ArgumentOutOfRangeException(
            nameof(modifier), modifier, "Неизвестный модификатор."),
    };

    #region Native interop

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nint Write(int fd, void* buf, nuint count);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ioctl(int fd, nuint request, int value);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ioctl(int fd, nuint request);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int Ioctl(int fd, nuint request, void* arg);

    private const int O_WRONLY = 1;
    private const int O_NONBLOCK = 0x800;

    private const nuint UI_DEV_CREATE = 0x5501;
    private const nuint UI_DEV_DESTROY = 0x5502;
    private const nuint UI_DEV_SETUP = 0x405C5503;
    private const nuint UI_SET_EVBIT = 0x40045564;
    private const nuint UI_SET_KEYBIT = 0x40045565;

    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort SYN_REPORT = 0x00;

    private const ushort BUS_USB = 0x03;
    private const ushort BUS_VIRTUAL = 0x06;

    private const int UINPUT_MAX_NAME_SIZE = 80;
    private const ushort KEY_MAX = 767;

    // --- Linux KEY_* scan codes ---

    private const ushort KEY_ESC = 1;
    private const ushort KEY_1 = 2;
    private const ushort KEY_2 = 3;
    private const ushort KEY_3 = 4;
    private const ushort KEY_4 = 5;
    private const ushort KEY_5 = 6;
    private const ushort KEY_6 = 7;
    private const ushort KEY_7 = 8;
    private const ushort KEY_8 = 9;
    private const ushort KEY_9 = 10;
    private const ushort KEY_0 = 11;
    private const ushort KEY_MINUS = 12;
    private const ushort KEY_EQUAL = 13;
    private const ushort KEY_BACKSPACE = 14;
    private const ushort KEY_TAB = 15;
    private const ushort KEY_Q = 16;
    private const ushort KEY_W = 17;
    private const ushort KEY_E = 18;
    private const ushort KEY_R = 19;
    private const ushort KEY_T = 20;
    private const ushort KEY_Y = 21;
    private const ushort KEY_U = 22;
    private const ushort KEY_I = 23;
    private const ushort KEY_O = 24;
    private const ushort KEY_P = 25;
    private const ushort KEY_LEFTBRACE = 26;
    private const ushort KEY_RIGHTBRACE = 27;
    private const ushort KEY_ENTER = 28;
    private const ushort KEY_LEFTCTRL = 29;
    private const ushort KEY_A = 30;
    private const ushort KEY_S = 31;
    private const ushort KEY_D = 32;
    private const ushort KEY_F = 33;
    private const ushort KEY_G = 34;
    private const ushort KEY_H = 35;
    private const ushort KEY_J = 36;
    private const ushort KEY_K = 37;
    private const ushort KEY_L = 38;
    private const ushort KEY_SEMICOLON = 39;
    private const ushort KEY_APOSTROPHE = 40;
    private const ushort KEY_GRAVE = 41;
    private const ushort KEY_LEFTSHIFT = 42;
    private const ushort KEY_BACKSLASH = 43;
    private const ushort KEY_Z = 44;
    private const ushort KEY_X = 45;
    private const ushort KEY_C = 46;
    private const ushort KEY_V = 47;
    private const ushort KEY_B = 48;
    private const ushort KEY_N = 49;
    private const ushort KEY_M = 50;
    private const ushort KEY_COMMA = 51;
    private const ushort KEY_DOT = 52;
    private const ushort KEY_SLASH = 53;
    private const ushort KEY_RIGHTSHIFT = 54;
    private const ushort KEY_KPASTERISK = 55;
    private const ushort KEY_LEFTALT = 56;
    private const ushort KEY_SPACE = 57;
    private const ushort KEY_F1 = 59;
    private const ushort KEY_F2 = 60;
    private const ushort KEY_F3 = 61;
    private const ushort KEY_F4 = 62;
    private const ushort KEY_F5 = 63;
    private const ushort KEY_F6 = 64;
    private const ushort KEY_F7 = 65;
    private const ushort KEY_F8 = 66;
    private const ushort KEY_F9 = 67;
    private const ushort KEY_F10 = 68;
    private const ushort KEY_KP7 = 71;
    private const ushort KEY_KP8 = 72;
    private const ushort KEY_KP9 = 73;
    private const ushort KEY_KPMINUS = 74;
    private const ushort KEY_KP4 = 75;
    private const ushort KEY_KP5 = 76;
    private const ushort KEY_KP6 = 77;
    private const ushort KEY_KPPLUS = 78;
    private const ushort KEY_KP1 = 79;
    private const ushort KEY_KP2 = 80;
    private const ushort KEY_KP3 = 81;
    private const ushort KEY_KP0 = 82;
    private const ushort KEY_KPDOT = 83;
    private const ushort KEY_F11 = 87;
    private const ushort KEY_F12 = 88;
    private const ushort KEY_KPSLASH = 98;
    private const ushort KEY_RIGHTCTRL = 97;
    private const ushort KEY_RIGHTALT = 100;
    private const ushort KEY_HOME = 102;
    private const ushort KEY_UP = 103;
    private const ushort KEY_PAGEUP = 104;
    private const ushort KEY_LEFT = 105;
    private const ushort KEY_RIGHT = 106;
    private const ushort KEY_END = 107;
    private const ushort KEY_DOWN = 108;
    private const ushort KEY_PAGEDOWN = 109;
    private const ushort KEY_INSERT = 110;
    private const ushort KEY_DELETE = 111;
    private const ushort KEY_PAUSE = 119;
    private const ushort KEY_LEFTMETA = 125;
    private const ushort KEY_RIGHTMETA = 126;
    private const ushort KEY_SYSRQ = 99;
    private const ushort KEY_F13 = 183;
    private const ushort KEY_F14 = 184;
    private const ushort KEY_F15 = 185;
    private const ushort KEY_F16 = 186;
    private const ushort KEY_F17 = 187;
    private const ushort KEY_F18 = 188;
    private const ushort KEY_F19 = 189;
    private const ushort KEY_F20 = 190;
    private const ushort KEY_F21 = 191;
    private const ushort KEY_F22 = 192;
    private const ushort KEY_F23 = 193;
    private const ushort KEY_F24 = 194;

    // --- Структуры ---

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public nint TimeSec;
        public nint TimeUsec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [InlineArray(UINPUT_MAX_NAME_SIZE)]
    private struct UinputName
    {
        private byte element;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputSetup
    {
        public InputId Id;
        public UinputName Name;
        public uint FfEffectsMax;
    }

    #endregion
}
