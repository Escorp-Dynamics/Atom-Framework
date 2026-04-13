#pragma warning disable CA1308, MA0051, S1144

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной мыши для Linux через /dev/uinput.
/// Создаёт виртуальное устройство ввода на уровне ядра.
/// Требует доступ к /dev/uinput (группа input или udev-правило).
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxMouseBackend : IVirtualMouseBackend
{
    private int fd = -1;
    private bool isDisposed;
    private nint xDisplay;
    private int mpxMasterPointerId = -1;
    private int originalMasterPointerId = -1;
    private int originalMasterKeyboardId = -1;

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasSeparateCursor => mpxMasterPointerId >= 0;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync(VirtualMouseSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        fd = Open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (fd < 0)
        {
            throw new VirtualMouseException(
                "/dev/uinput недоступен. Убедитесь что:" + Environment.NewLine +
                "  1. Модуль ядра загружен: sudo modprobe uinput" + Environment.NewLine +
                "  2. Пользователь в группе input: sudo usermod -aG input $USER" + Environment.NewLine +
                "  3. udev-правило: KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\"");
        }

        try
        {
            ConfigureDevice(settings);
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

        // Даём udev время зарегистрировать устройство.
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        if (settings.UseSeparateCursor)
        {
            // Настраиваем отдельный курсор через Multi-Pointer X (MPX).
            await SetupSeparateCursorAsync(settings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void MoveAbsolute(Point position)
    {
        ThrowIfDisposed();
        WriteEvent(EV_ABS, ABS_X, position.X);
        WriteEvent(EV_ABS, ABS_Y, position.Y);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void MoveRelative(Size delta)
    {
        ThrowIfDisposed();
        WriteEvent(EV_REL, REL_X, delta.Width);
        WriteEvent(EV_REL, REL_Y, delta.Height);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void ButtonDown(VirtualMouseButton button)
    {
        ThrowIfDisposed();
        WriteEvent(EV_KEY, MapButton(button), 1);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void ButtonUp(VirtualMouseButton button)
    {
        ThrowIfDisposed();
        WriteEvent(EV_KEY, MapButton(button), 0);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void Scroll(int delta)
    {
        ThrowIfDisposed();
        WriteEvent(EV_REL, REL_WHEEL, delta);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public void ScrollHorizontal(int delta)
    {
        ThrowIfDisposed();
        WriteEvent(EV_REL, REL_HWHEEL, delta);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true))
        {
            return ValueTask.CompletedTask;
        }

        CleanupMpx();

        if (fd >= 0)
        {
            _ = Ioctl(fd, UI_DEV_DESTROY);
            _ = Close(fd);
            fd = -1;
        }

        return ValueTask.CompletedTask;
    }

    private void ConfigureDevice(VirtualMouseSettings settings)
    {
        // Включаем типы событий.
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_KEY, "UI_SET_EVBIT(EV_KEY)");
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_ABS, "UI_SET_EVBIT(EV_ABS)");
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_REL, "UI_SET_EVBIT(EV_REL)");
        IoctlOrThrow(fd, UI_SET_EVBIT, EV_SYN, "UI_SET_EVBIT(EV_SYN)");

        // Кнопки мыши.
        IoctlOrThrow(fd, UI_SET_KEYBIT, BTN_LEFT, "UI_SET_KEYBIT(BTN_LEFT)");
        IoctlOrThrow(fd, UI_SET_KEYBIT, BTN_RIGHT, "UI_SET_KEYBIT(BTN_RIGHT)");
        IoctlOrThrow(fd, UI_SET_KEYBIT, BTN_MIDDLE, "UI_SET_KEYBIT(BTN_MIDDLE)");

        // Абсолютные оси.
        IoctlOrThrow(fd, UI_SET_ABSBIT, ABS_X, "UI_SET_ABSBIT(ABS_X)");
        IoctlOrThrow(fd, UI_SET_ABSBIT, ABS_Y, "UI_SET_ABSBIT(ABS_Y)");

        // Относительные оси (для MoveRelative и Scroll).
        IoctlOrThrow(fd, UI_SET_RELBIT, REL_X, "UI_SET_RELBIT(REL_X)");
        IoctlOrThrow(fd, UI_SET_RELBIT, REL_Y, "UI_SET_RELBIT(REL_Y)");
        IoctlOrThrow(fd, UI_SET_RELBIT, REL_WHEEL, "UI_SET_RELBIT(REL_WHEEL)");
        IoctlOrThrow(fd, UI_SET_RELBIT, REL_HWHEEL, "UI_SET_RELBIT(REL_HWHEEL)");

        // Настраиваем диапазон абсолютных осей.
        SetupAbsAxis(ABS_X, 0, settings.ScreenSize.Width - 1);
        SetupAbsAxis(ABS_Y, 0, settings.ScreenSize.Height - 1);
    }

    private unsafe void SetupAbsAxis(ushort code, int minimum, int maximum)
    {
        var setup = new UinputAbsSetup
        {
            Code = code,
            AbsInfo = new InputAbsInfo
            {
                Minimum = minimum,
                Maximum = maximum,
            },
        };

        if (Ioctl(fd, UI_ABS_SETUP, &setup) < 0)
        {
            throw new VirtualMouseException("ioctl UI_ABS_SETUP не удался для оси " + code + ".");
        }
    }

    private unsafe void SetupDevice(VirtualMouseSettings settings)
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
            throw new VirtualMouseException("ioctl UI_DEV_SETUP не удался.");
        }
    }

    private void CreateDevice()
    {
        if (Ioctl(fd, UI_DEV_CREATE) < 0)
        {
            throw new VirtualMouseException("ioctl UI_DEV_CREATE не удался.");
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
            throw new VirtualMouseException(
                "Не удалось записать событие в /dev/uinput.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

    private static void IoctlOrThrow(int deviceFd, nuint request, int value, string description)
    {
        if (Ioctl(deviceFd, request, value) < 0)
        {
            throw new VirtualMouseException("ioctl " + description + " не удался.");
        }
    }

    private static ushort MapButton(VirtualMouseButton button) => button switch
    {
        VirtualMouseButton.Left => BTN_LEFT,
        VirtualMouseButton.Right => BTN_RIGHT,
        VirtualMouseButton.Middle => BTN_MIDDLE,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Неизвестная кнопка мыши."),
    };

    /// <summary>
    /// Определяет полный размер виртуального экрана X11 (сумма всех мониторов).
    /// Возвращает <see langword="null"/>, если X11 недоступен.
    /// </summary>
    internal static Size? DetectVirtualScreenSize()
    {
        var display = nint.Zero;
        try
        {
            display = XOpenDisplay(displayName: null);
            if (display == nint.Zero)
                return null;

            var screen = XDefaultScreen(display);
            var width = XDisplayWidth(display, screen);
            var height = XDisplayHeight(display, screen);

            return width > 0 && height > 0 ? new Size(width, height) : null;
        }
        finally
        {
            if (display != nint.Zero)
                _ = XCloseDisplay(display);
        }
    }

    #region Multi-Pointer X (MPX)

    private async ValueTask SetupSeparateCursorAsync(VirtualMouseSettings settings, CancellationToken cancellationToken)
    {
        // MPX работает только на нативном Xorg.
        // XWayland не проксирует uinput-устройства в XInput2 —
        // все события идут через единый xwayland-pointer.
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
            return;

        xDisplay = XOpenDisplay(displayName: null);
        if (xDisplay == nint.Zero) return;

        var success = false;
        try
        {
            // Ожидаем появления устройства в X11 (до 500 мс).
            var slaveId = -1;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                slaveId = FindSlaveDevice(settings.Name, out var attachedMaster);
                if (slaveId >= 0)
                {
                    originalMasterPointerId = attachedMaster;
                    break;
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }

            if (slaveId < 0) return;

            FindOriginalMasterKeyboard();

            if (!CreateMasterPointerDevice(settings.Name)) return;

            mpxMasterPointerId = FindMasterDevice(settings.Name + " pointer", XI_MASTER_POINTER);
            if (mpxMasterPointerId < 0) return;

            AttachSlaveToMaster(slaveId, mpxMasterPointerId);
            success = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable ERP022, S2486, S108 // MPX — опциональная функция; устройство продолжит работать с общим курсором.
        catch
        {
        }
#pragma warning restore ERP022, S2486, S108
        finally
        {
            if (!success) CleanupMpx();
        }
    }

    private unsafe int FindSlaveDevice(string deviceName, out int attachedMaster)
    {
        attachedMaster = -1;
        var nDevices = 0;
        var devices = XIQueryDevice(xDisplay, XI_ALL_DEVICES, &nDevices);
        if (devices == null) return -1;

        try
        {
            for (var i = 0; i < nDevices; i++)
            {
                if (devices[i].Use != XI_SLAVE_POINTER) continue;

                var name = Marshal.PtrToStringUTF8(devices[i].Name);
                if (string.Equals(name, deviceName, StringComparison.Ordinal))
                {
                    attachedMaster = devices[i].Attachment;
                    return devices[i].DeviceId;
                }
            }

            return -1;
        }
        finally
        {
            XIFreeDeviceInfo(devices);
        }
    }

    private unsafe void FindOriginalMasterKeyboard()
    {
        if (originalMasterPointerId < 0) return;

        int nDevices;
        var devices = XIQueryDevice(xDisplay, originalMasterPointerId, &nDevices);
        if (devices == null) return;

        try
        {
            if (nDevices > 0 && devices[0].Use == XI_MASTER_POINTER)
                originalMasterKeyboardId = devices[0].Attachment;
        }
        finally
        {
            XIFreeDeviceInfo(devices);
        }
    }

    private unsafe bool CreateMasterPointerDevice(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* namePtr = nameBytes)
        {
            var add = new XIAddMasterInfo
            {
                Type = XI_ADD_MASTER,
                Name = (nint)namePtr,
                SendCore = 1,
                Enable = 1,
            };

            var result = XIChangeHierarchy(xDisplay, &add, 1);
            _ = XSync(xDisplay, 0);
            return result == 0;
        }
    }

    private unsafe int FindMasterDevice(string deviceName, int use)
    {
        var nDevices = 0;
        var devices = XIQueryDevice(xDisplay, XI_ALL_DEVICES, &nDevices);
        if (devices == null) return -1;

        try
        {
            for (var i = 0; i < nDevices; i++)
            {
                if (devices[i].Use != use) continue;

                var name = Marshal.PtrToStringUTF8(devices[i].Name);
                if (string.Equals(name, deviceName, StringComparison.Ordinal))
                    return devices[i].DeviceId;
            }

            return -1;
        }
        finally
        {
            XIFreeDeviceInfo(devices);
        }
    }

    private unsafe void AttachSlaveToMaster(int slaveId, int masterId)
    {
        var attach = new XIAttachSlaveInfo
        {
            Type = XI_ATTACH_SLAVE,
            DeviceId = slaveId,
            NewMaster = masterId,
        };

        _ = XIChangeHierarchy(xDisplay, &attach, 1);
        _ = XSync(xDisplay, 0);
    }

    private unsafe void CleanupMpx()
    {
        if (xDisplay == nint.Zero) return;

        if (mpxMasterPointerId >= 0)
        {
            var remove = new XIRemoveMasterInfo
            {
                Type = XI_REMOVE_MASTER,
                DeviceId = mpxMasterPointerId,
                ReturnMode = XI_ATTACH_TO_MASTER,
                ReturnPointer = originalMasterPointerId >= 0 ? originalMasterPointerId : 2,
                ReturnKeyboard = originalMasterKeyboardId >= 0 ? originalMasterKeyboardId : 3,
            };

            _ = XIChangeHierarchy(xDisplay, &remove, 1);
            _ = XSync(xDisplay, 0);
            mpxMasterPointerId = -1;
        }

        _ = XCloseDisplay(xDisplay);
        xDisplay = nint.Zero;
    }

    #endregion

    #region Native interop

    // --- X11 ---

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDisplayWidth")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDisplayWidth(nint display, int screenNumber);

    [LibraryImport("libX11.so.6", EntryPoint = "XDisplayHeight")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDisplayHeight(nint display, int screenNumber);

    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XSync(nint display, int discard);

    // --- libXi (XInput2) ---

    [LibraryImport("libXi.so.6", EntryPoint = "XIQueryDevice")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial XIDeviceInfo* XIQueryDevice(nint display, int deviceId, int* nDevicesReturn);

    [LibraryImport("libXi.so.6", EntryPoint = "XIFreeDeviceInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial void XIFreeDeviceInfo(XIDeviceInfo* info);

    [LibraryImport("libXi.so.6", EntryPoint = "XIChangeHierarchy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int XIChangeHierarchy(nint display, void* changes, int numChanges);

    // --- Syscalls ---

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

    // --- Константы open ---

    private const int O_WRONLY = 1;
    private const int O_NONBLOCK = 0x800;

    // --- Константы ioctl uinput ---

    // _IO('U', 1)
    private const nuint UI_DEV_CREATE = 0x5501;
    // _IO('U', 2)
    private const nuint UI_DEV_DESTROY = 0x5502;
    // _IOW('U', 3, struct uinput_setup{92})
    private const nuint UI_DEV_SETUP = 0x405C5503;
    // _IOW('U', 4, struct uinput_abs_setup{28})
    private const nuint UI_ABS_SETUP = 0x401C5504;
    // _IOW('U', 100, int)
    private const nuint UI_SET_EVBIT = 0x40045564;
    // _IOW('U', 101, int)
    private const nuint UI_SET_KEYBIT = 0x40045565;
    // _IOW('U', 102, int)
    private const nuint UI_SET_RELBIT = 0x40045566;
    // _IOW('U', 103, int)
    private const nuint UI_SET_ABSBIT = 0x40045567;

    // --- Типы событий ---

    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;
    private const ushort EV_ABS = 0x03;

    // --- Коды синхронизации ---

    private const ushort SYN_REPORT = 0x00;

    // --- Коды кнопок ---

    private const ushort BTN_LEFT = 0x110;
    private const ushort BTN_RIGHT = 0x111;
    private const ushort BTN_MIDDLE = 0x112;

    // --- Коды абсолютных осей ---

    private const ushort ABS_X = 0x00;
    private const ushort ABS_Y = 0x01;

    // --- Коды относительных осей ---

    private const ushort REL_X = 0x00;
    private const ushort REL_Y = 0x01;
    private const ushort REL_HWHEEL = 0x06;
    private const ushort REL_WHEEL = 0x08;

    // --- Типы шин ---

    private const ushort BUS_USB = 0x03;
    private const ushort BUS_VIRTUAL = 0x06;

    // --- XInput2 ---

    private const int XI_ALL_DEVICES = 0;
    private const int XI_MASTER_POINTER = 1;
    private const int XI_SLAVE_POINTER = 3;
    private const int XI_ADD_MASTER = 1;
    private const int XI_REMOVE_MASTER = 2;
    private const int XI_ATTACH_SLAVE = 3;
    private const int XI_ATTACH_TO_MASTER = 1;

    // --- Размер имени ---

    private const int UINPUT_MAX_NAME_SIZE = 80;

    // --- Структуры ---

    /// <summary>
    /// struct input_event для 64-bit Linux.
    /// </summary>
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

    [StructLayout(LayoutKind.Sequential)]
    private struct InputAbsInfo
    {
        public int Value;
        public int Minimum;
        public int Maximum;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputAbsSetup
    {
        public ushort Code;
        private readonly ushort padding;
        public InputAbsInfo AbsInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIDeviceInfo
    {
        public int DeviceId;
        public nint Name;
        public int Use;
        public int Attachment;
        public int Enabled;
        public int NumClasses;
        public nint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIAddMasterInfo
    {
        public int Type;
        public nint Name;
        public int SendCore;
        public int Enable;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIRemoveMasterInfo
    {
        public int Type;
        public int DeviceId;
        public int ReturnMode;
        public int ReturnPointer;
        public int ReturnKeyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XIAttachSlaveInfo
    {
        public int Type;
        public int DeviceId;
        public int NewMaster;
    }

    #endregion
}
