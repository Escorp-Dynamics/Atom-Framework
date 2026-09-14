using System.Drawing;
using System.Runtime.Versioning;

using Atom.Hardware.Input.Uinput;

namespace Atom.Hardware.Input.Touch.Backends;

/// <summary>
/// Виртуальный тачскрин поверх общего uinput-транспорта.
/// </summary>
/// <remarks>
/// Протокол — multitouch типа B (<c lang="text">ABS_MT_SLOT</c> + <c lang="text">ABS_MT_TRACKING_ID</c>), тот же,
/// что у настоящих сенсорных панелей. Протокол A ядро считает устаревшим, и по одному этому
/// устройство было бы отличимо от современного железа.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class UinputTouchscreenBackend : IVirtualTouchscreenBackend
{
    private UinputDevice? device;
    private Size screenSize;
    private int nextTrackingId;

    /// <inheritdoc/>
    public string DeviceIdentifier => device?.DeviceIdentifier ?? string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync(VirtualTouchscreenSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        screenSize = settings.ScreenSize;

        var maxX = Math.Max(1, settings.ScreenSize.Width - 1);
        var maxY = Math.Max(1, settings.ScreenSize.Height - 1);
        var slots = Math.Max(1, settings.MaxTouchPoints) - 1;

        device = await UinputDevice.CreateAsync(
            new UinputDeviceDescriptor
            {
                Name = settings.Name,
                BusType = settings.UsbVendorId.HasValue ? UinputCodes.BUS_USB : UinputCodes.BUS_VIRTUAL,
                VendorId = settings.UsbVendorId ?? 0,
                ProductId = settings.UsbProductId ?? 0,
                EventTypes = [UinputCodes.EV_SYN, UinputCodes.EV_KEY, UinputCodes.EV_ABS],

                // BTN_TOUCH обязателен: по нему libinput отличает тачскрин от планшета-дигитайзера.
                Keys = [UinputCodes.BTN_TOUCH],
                Properties = [UinputCodes.INPUT_PROP_DIRECT],
                AbsoluteAxes =
                [
                    new(UinputCodes.ABS_X, 0, maxX),
                    new(UinputCodes.ABS_Y, 0, maxY),
                    new(UinputCodes.ABS_MT_POSITION_X, 0, maxX),
                    new(UinputCodes.ABS_MT_POSITION_Y, 0, maxY),
                    new(UinputCodes.ABS_MT_SLOT, 0, slots),
                    new(UinputCodes.ABS_MT_TRACKING_ID, 0, 65535),
                    new(UinputCodes.ABS_MT_TOUCH_MAJOR, 0, 255),
                    new(UinputCodes.ABS_MT_PRESSURE, 0, 255),
                ],
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask TapAsync(Point point, TimeSpan holdDuration, CancellationToken cancellationToken)
    {
        var target = RequireDevice();
        cancellationToken.ThrowIfCancellationRequested();

        BeginTouch(target, point);

        await Task.Delay(holdDuration > TimeSpan.Zero ? holdDuration : DefaultHoldDuration, cancellationToken).ConfigureAwait(false);

        EndTouch(target);
    }

    /// <inheritdoc/>
    public async ValueTask SwipeAsync(IReadOnlyList<Point> path, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var target = RequireDevice();
        cancellationToken.ThrowIfCancellationRequested();

        if (path.Count < 2)
            throw new ArgumentException("Для проведения нужны минимум две точки.", nameof(path));

        // Палец опускается в первой точке и НЕ отрывается до последней: разрыв контакта посередине
        // ядро считает двумя разными касаниями, и жест распадается на два тапа.
        BeginTouch(target, path[0]);

        var total = duration > TimeSpan.Zero ? duration : DefaultSwipeDuration;
        var stepDelay = TimeSpan.FromMilliseconds(total.TotalMilliseconds / (path.Count - 1));

        for (var index = 1; index < path.Count; ++index)
        {
            await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
            MoveTouch(target, path[index]);
        }

        EndTouch(target);
    }

    private void BeginTouch(UinputDevice target, Point point)
    {
        var (x, y) = Clamp(point);
        var trackingId = Interlocked.Increment(ref nextTrackingId);

        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_SLOT, 0);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_TRACKING_ID, trackingId);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_POSITION_X, x);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_POSITION_Y, y);

        // Площадь пятна и давление: касание без них выглядит как «идеальная точка», чего у пальца
        // не бывает.
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_TOUCH_MAJOR, TouchMajor);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_PRESSURE, TouchPressure);

        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_X, x);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_Y, y);
        target.Write(UinputCodes.EV_KEY, UinputCodes.BTN_TOUCH, 1);
        target.Sync();
    }

    private void MoveTouch(UinputDevice target, Point point)
    {
        var (x, y) = Clamp(point);

        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_SLOT, 0);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_POSITION_X, x);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_POSITION_Y, y);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_X, x);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_Y, y);
        target.Sync();
    }

    private static void EndTouch(UinputDevice target)
    {
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_SLOT, 0);
        target.Write(UinputCodes.EV_ABS, UinputCodes.ABS_MT_TRACKING_ID, -1);
        target.Write(UinputCodes.EV_KEY, UinputCodes.BTN_TOUCH, 0);
        target.Sync();
    }

    private (int X, int Y) Clamp(Point point) => (
        Math.Clamp(point.X, 0, Math.Max(0, screenSize.Width - 1)),
        Math.Clamp(point.Y, 0, Math.Max(0, screenSize.Height - 1)));

    private UinputDevice RequireDevice()
        => device ?? throw new VirtualTouchscreenException("Тачскрин не инициализирован.");

    /// <summary>Сколько держится палец при обычном тапе.</summary>
    private static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMilliseconds(85);

    /// <summary>Длительность проведения по умолчанию.</summary>
    private static readonly TimeSpan DefaultSwipeDuration = TimeSpan.FromMilliseconds(280);

    /// <summary>Поперечник пятна касания в единицах матрицы.</summary>
    private const int TouchMajor = 6;

    /// <summary>Давление касания.</summary>
    private const int TouchPressure = 42;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (device is { } target)
        {
            await target.DisposeAsync().ConfigureAwait(false);
            device = null;
        }
    }
}
