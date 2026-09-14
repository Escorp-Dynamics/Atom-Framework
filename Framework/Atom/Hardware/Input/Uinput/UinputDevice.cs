// Имена констант и поля структур повторяют ABI ядра и меняться не могут.
#pragma warning disable CA1707, S1144, S3604

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Atom.Hardware.Input.Uinput;

/// <summary>
/// Устройство ввода уровня ядра через <c lang="text">/dev/uinput</c>.
/// </summary>
/// <remarks>
/// Один транспорт на все виртуальные устройства: мышь, клавиатуру и тачскрин. Он же —
/// единственный способ отдать браузеру ДОВЕРЕННЫЙ ввод: события из JS приходят с
/// <c lang="text">isTrusted = false</c>, а CDP выдаёт автоматизацию с головой.
///
/// Почему не XTEST, которым сейчас ходят мышь и клавиатура: у него нет API для касаний вовсе
/// (<c lang="text">libXtst</c> экспортирует только Button/Key/Motion/Proximity), то есть тачскрин через него
/// невозможен в принципе. uinput же есть в ядре любого Linux (<c lang="text">CONFIG_INPUT_UINPUT</c>),
/// не требует пакетов и одинаково обслуживает все три устройства.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed partial class UinputDevice : IAsyncDisposable
{
    private int fd = -1;
    private int isDisposed;

    private UinputDevice(int fd, string identifier)
    {
        this.fd = fd;
        DeviceIdentifier = identifier;
    }

    /// <summary>Идентификатор устройства в системе.</summary>
    public string DeviceIdentifier { get; }

    /// <summary>
    /// Открывает <c lang="text">/dev/uinput</c> и настраивает устройство по описанию.
    /// </summary>
    /// <param name="descriptor">Описание создаваемого устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async ValueTask<UinputDevice> CreateAsync(UinputDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        var handle = Open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (handle < 0)
            throw new UinputException(DescribeOpenFailure(Marshal.GetLastWin32Error()));

        try
        {
            Configure(handle, descriptor);
            Setup(handle, descriptor);

            if (Ioctl(handle, UI_DEV_CREATE) < 0)
                throw new UinputException("ioctl UI_DEV_CREATE не удался.");
        }
        catch
        {
            _ = Close(handle);
            throw;
        }

        var device = new UinputDevice(handle, "uinput:" + descriptor.Name);

        // udev должен успеть создать узел и применить правила: без паузы первое событие уходит
        // в никуда, а свойства устройства (в том числе привязка к выходу) ещё не проставлены.
        await Task.Delay(descriptor.RegistrationDelay, cancellationToken).ConfigureAwait(false);

        return device;
    }

    /// <summary>Отправляет одно событие ядру.</summary>
    public unsafe void Write(ushort type, ushort code, int value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);

        var ev = new InputEvent
        {
            Type = type,
            Code = code,
            Value = value,
        };

        if (WriteCore(fd, &ev, (nuint)sizeof(InputEvent)) < 0)
            throw new UinputException("Не удалось записать событие в /dev/uinput.");
    }

    /// <summary>Завершает пакет событий: до этого ядро их не публикует.</summary>
    public void Sync() => Write(UinputCodes.EV_SYN, UinputCodes.SYN_REPORT, 0);

    private static void Configure(int handle, UinputDeviceDescriptor descriptor)
    {
        foreach (var eventType in descriptor.EventTypes)
            IoctlOrThrow(handle, UI_SET_EVBIT, eventType, "UI_SET_EVBIT");

        foreach (var key in descriptor.Keys)
            IoctlOrThrow(handle, UI_SET_KEYBIT, key, "UI_SET_KEYBIT");

        foreach (var axis in descriptor.RelativeAxes)
            IoctlOrThrow(handle, UI_SET_RELBIT, axis, "UI_SET_RELBIT");

        foreach (var property in descriptor.Properties)
            IoctlOrThrow(handle, UI_SET_PROPBIT, property, "UI_SET_PROPBIT");

        foreach (var axis in descriptor.AbsoluteAxes)
        {
            IoctlOrThrow(handle, UI_SET_ABSBIT, axis.Code, "UI_SET_ABSBIT");
            SetupAbsoluteAxis(handle, axis);
        }
    }

    private static unsafe void SetupAbsoluteAxis(int handle, UinputAbsoluteAxis axis)
    {
        var setup = new UinputAbsSetup
        {
            Code = axis.Code,
            AbsInfo = new InputAbsInfo
            {
                Minimum = axis.Minimum,
                Maximum = axis.Maximum,
                Resolution = axis.Resolution,
            },
        };

        if (Ioctl(handle, UI_ABS_SETUP, &setup) < 0)
            throw new UinputException("ioctl UI_ABS_SETUP не удался для оси " + axis.Code.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    private static unsafe void Setup(int handle, UinputDeviceDescriptor descriptor)
    {
        var setup = new UinputSetup();

        setup.Id.BusType = descriptor.BusType;
        setup.Id.Vendor = descriptor.VendorId;
        setup.Id.Product = descriptor.ProductId;
        setup.Id.Version = descriptor.Version;

        var nameBytes = Encoding.UTF8.GetBytes(descriptor.Name);
        var copyLength = Math.Min(nameBytes.Length, UINPUT_MAX_NAME_SIZE - 1);
        Span<byte> nameSpan = setup.Name;
        nameBytes.AsSpan(0, copyLength).CopyTo(nameSpan);
        nameSpan[copyLength] = 0;

        if (Ioctl(handle, UI_DEV_SETUP, &setup) < 0)
            throw new UinputException("ioctl UI_DEV_SETUP не удался.");
    }

    /// <summary>
    /// Объясняет отказ открытия по errno.
    /// </summary>
    /// <remarks>
    /// Причины принципиально разные, и общий совет «добавьте себя в группу input» уводит в сторону:
    /// при ENODEV права ни при чём — модуль не загружен либо каталог модулей работающего ядра
    /// исчез после обновления, и тогда нужен modprobe или перезагрузка.
    /// </remarks>
    private static string DescribeOpenFailure(int errno) => errno switch
    {
        ENODEV or ENXIO =>
            "/dev/uinput есть, но драйвера за ним нет. Загрузите модуль: sudo modprobe uinput. "
            + "Если modprobe сообщает «Module uinput not found», каталог модулей работающего ядра "
            + "исчез после обновления — нужна перезагрузка.",
        EACCES or EPERM =>
            "/dev/uinput недоступен по правам. Добавьте пользователя в группу input "
            + "(sudo usermod -aG input $USER) либо задайте udev-правило: "
            + "KERNEL==\"uinput\", GROUP=\"input\", MODE=\"0660\".",
        ENOENT => "/dev/uinput не существует. Загрузите модуль: sudo modprobe uinput.",
        _ => "/dev/uinput не удалось открыть, errno " + errno.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
    };

    private static void IoctlOrThrow(int handle, nuint request, int value, string description)
    {
        if (Ioctl(handle, request, value) < 0)
            throw new UinputException("ioctl " + description + " не удался.");
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return ValueTask.CompletedTask;

        if (fd >= 0)
        {
            _ = Ioctl(fd, UI_DEV_DESTROY);
            _ = Close(fd);
            fd = -1;
        }

        return ValueTask.CompletedTask;
    }

    private const nuint UI_DEV_CREATE = 0x5501;
    private const nuint UI_DEV_DESTROY = 0x5502;
    private const nuint UI_DEV_SETUP = 0x405C5503;
    private const nuint UI_ABS_SETUP = 0x401C5504;
    private const nuint UI_SET_EVBIT = 0x40045564;
    private const nuint UI_SET_KEYBIT = 0x40045565;
    private const nuint UI_SET_RELBIT = 0x40045566;
    private const nuint UI_SET_ABSBIT = 0x40045567;
    private const nuint UI_SET_PROPBIT = 0x4004556E;

    private const int O_WRONLY = 0x01;
    private const int O_NONBLOCK = 0x800;

    private const int EPERM = 1;
    private const int ENOENT = 2;
    private const int ENXIO = 6;
    private const int EACCES = 13;
    private const int ENODEV = 19;

    private const int UINPUT_MAX_NAME_SIZE = 80;

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public nint TimeSeconds;
        public nint TimeMicroseconds;
        public ushort Type;
        public ushort Code;
        public int Value;
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
    private struct InputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    [System.Runtime.CompilerServices.InlineArray(UINPUT_MAX_NAME_SIZE)]
    private struct UinputName
    {
        private byte element;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputSetup
    {
        public InputId Id;
        public UinputName Name;
        public uint EffectsMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputAbsSetup
    {
        public ushort Code;
        private readonly ushort padding;
        public InputAbsInfo AbsInfo;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ioctl(int fd, nuint request);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ioctl(int fd, nuint request, int value);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int Ioctl(int fd, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nint WriteCore(int fd, void* buffer, nuint count);
}
