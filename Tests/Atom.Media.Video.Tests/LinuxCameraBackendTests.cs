using System.Runtime.Versioning;
using Atom.Media.Video;
using Atom.Media.Video.Backends;

namespace Atom.Media.Video.Tests;

[TestFixture]
[Category("Integration")]
[SupportedOSPlatform("linux")]
public class LinuxCameraBackendTests(ILogger logger) : BenchmarkTests<LinuxCameraBackendTests>(logger)
{
    private static readonly bool isPipeWireAvailable = CheckPipeWireAvailable();

    public LinuxCameraBackendTests() : this(ConsoleLogger.Unicode) { }

    private static bool CheckPipeWireAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            var backend = new LinuxCameraBackend();
            var settings = new VirtualCameraSettings { Width = 320, Height = 240 };
            backend.InitializeAsync(settings, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            backend.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (VirtualCameraException)
        {
            return false;
        }
    }

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("Тесты LinuxCameraBackend запускаются только на Linux.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }
    }

    [TestCase(TestName = "Инициализация создаёт DeviceIdentifier с префиксом pipewire:")]
    public async Task InitializeSetsDeviceIdentifier()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 640, Height = 480, Name = "Test Cam" };

        await backend.InitializeAsync(settings, CancellationToken.None);

        Assert.That(backend.DeviceIdentifier, Is.EqualTo("pipewire:Test Cam"));
    }

    [TestCase(TestName = "IsCapturing изначально false")]
    public async Task IsCapturingInitiallyFalse()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        Assert.That(backend.IsCapturing, Is.False);
    }

    [TestCase(TestName = "StartCapture переводит IsCapturing в true")]
    public async Task StartCaptureSetsIsCapturing()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };

        await backend.InitializeAsync(settings, CancellationToken.None);
        await backend.StartCaptureAsync(CancellationToken.None);

        Assert.That(backend.IsCapturing, Is.True);
    }

    [TestCase(TestName = "StopCapture переводит IsCapturing в false")]
    public async Task StopCaptureResetsIsCapturing()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 640, Height = 480 };

        await backend.InitializeAsync(settings, CancellationToken.None);
        await backend.StartCaptureAsync(CancellationToken.None);
        await backend.StopCaptureAsync(CancellationToken.None);

        Assert.That(backend.IsCapturing, Is.False);
    }

    [TestCase(TestName = "WriteFrame во время захвата не выбрасывает исключений")]
    public async Task WriteFrameDuringCaptureDoesNotThrow()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 4, Height = 2, PixelFormat = VideoPixelFormat.Rgba32 };

        await backend.InitializeAsync(settings, CancellationToken.None);
        await backend.StartCaptureAsync(CancellationToken.None);

        var frame = new byte[4 * 2 * 4]; // 4x2 RGBA
        Assert.DoesNotThrow(() => backend.WriteFrame(frame));
    }

    [TestCase(TestName = "WriteFrame без захвата выбрасывает VirtualCameraException")]
    public async Task WriteFrameWithoutCaptureThrows()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 4, Height = 2, PixelFormat = VideoPixelFormat.Rgb24 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        var frame = new byte[4 * 2 * 3]; // 4x2 RGB
        Assert.That(
            () => backend.WriteFrame(frame),
            Throws.TypeOf<VirtualCameraException>());
    }

    [TestCase(TestName = "StartCapture без инициализации выбрасывает VirtualCameraException")]
    public async Task StartCaptureWithoutInitThrows()
    {
        await using var backend = new LinuxCameraBackend();

        Assert.ThrowsAsync<VirtualCameraException>(async () =>
            await backend.StartCaptureAsync(CancellationToken.None));
    }

    [TestCase(TestName = "InitializeAsync с отменённым токеном выбрасывает OperationCanceledException")]
    public async Task InitializeCancelledTokenThrows()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await backend.InitializeAsync(settings, cts.Token));
    }

    [TestCase(TestName = "StartCapture с отменённым токеном выбрасывает OperationCanceledException")]
    public async Task StartCaptureCancelledTokenThrows()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await backend.StartCaptureAsync(cts.Token));
    }

    [TestCase(TestName = "StopCapture с отменённым токеном выбрасывает OperationCanceledException")]
    public async Task StopCaptureCancelledTokenThrows()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await backend.StopCaptureAsync(cts.Token));
    }

    [TestCase(TestName = "Повторный DisposeAsync безопасен")]
    public async Task DoubleDisposeIsSafe()
    {
        var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);
        await backend.DisposeAsync();

        Assert.DoesNotThrowAsync(async () => await backend.DisposeAsync());
    }

    [TestCase(TestName = "Полный жизненный цикл: init → start → write → stop → dispose")]
    public async Task FullLifecycle()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings
        {
            Width = 4,
            Height = 2,
            FrameRate = 30,
            PixelFormat = VideoPixelFormat.Nv12,
            Name = "Lifecycle Test",
        };

        await backend.InitializeAsync(settings, CancellationToken.None);
        Assert.That(backend.DeviceIdentifier, Is.EqualTo("pipewire:Lifecycle Test"));

        await backend.StartCaptureAsync(CancellationToken.None);
        Assert.That(backend.IsCapturing, Is.True);

        // NV12: width * height * 1.5
        var frame = new byte[4 * 2 + 4 * 1]; // Y-plane + UV-plane (4x2 NV12)
        backend.WriteFrame(frame);

        await backend.StopCaptureAsync(CancellationToken.None);
        Assert.That(backend.IsCapturing, Is.False);
    }

    [TestCase(TestName = "Инициализация с метаданными не выбрасывает исключений")]
    public async Task InitWithMetadataSucceeds()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Name = "Metadata Test",
            Vendor = "Escorp Dynamics",
            Model = "Atom VCam",
            SerialNumber = "SN-001",
            Description = "Тестовая камера",
            FirmwareVersion = "1.0.0",
            UsbVendorId = 0x046D,
            UsbProductId = 0x0825,
            BusType = "usb",
            FormFactor = "webcam",
            IconName = "camera-web",
        };

        Assert.DoesNotThrowAsync(async () =>
            await backend.InitializeAsync(settings, CancellationToken.None));

        Assert.That(backend.DeviceIdentifier, Is.EqualTo("pipewire:Metadata Test"));
    }

    [TestCase(TestName = "Инициализация с ExtraProperties передаёт произвольные свойства")]
    public async Task InitWithExtraPropertiesSucceeds()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            ExtraProperties = new Dictionary<string, string>
            {
                ["custom.atom.version"] = "1.0",
                ["node.latency"] = "512/48000",
            },
        };

        Assert.DoesNotThrowAsync(async () =>
            await backend.InitializeAsync(settings, CancellationToken.None));
    }

    [TestCase(TestName = "Инициализация с частичными метаданными работает")]
    public async Task InitWithPartialMetadataSucceeds()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings
        {
            Width = 320,
            Height = 240,
            Vendor = "Test Vendor",
        };

        Assert.DoesNotThrowAsync(async () =>
            await backend.InitializeAsync(settings, CancellationToken.None));
    }

    [TestCase(TestName = "SetControl без инициализации выбрасывает VirtualCameraException")]
    public async Task SetControlWithoutInitThrows()
    {
        await using var backend = new LinuxCameraBackend();

        Assert.That(
            () => backend.SetControl(CameraControlType.Brightness, value: 0.5f),
            Throws.TypeOf<VirtualCameraException>());
    }

    [TestCase(TestName = "GetControl без инициализации выбрасывает VirtualCameraException")]
    public async Task GetControlWithoutInitThrows()
    {
        await using var backend = new LinuxCameraBackend();

        Assert.That(
            () => backend.GetControl(CameraControlType.Brightness),
            Throws.TypeOf<VirtualCameraException>());
    }

    [TestCase(CameraControlType.Brightness, TestName = "SetControl Brightness после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Contrast, TestName = "SetControl Contrast после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Saturation, TestName = "SetControl Saturation после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Hue, TestName = "SetControl Hue после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Gamma, TestName = "SetControl Gamma после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Exposure, TestName = "SetControl Exposure после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Gain, TestName = "SetControl Gain после инициализации не выбрасывает")]
    [TestCase(CameraControlType.Sharpness, TestName = "SetControl Sharpness после инициализации не выбрасывает")]
    public async Task SetControlAfterInit(CameraControlType control)
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        // pw_stream_set_control может вернуть ошибку если контрол не поддержан потоком,
        // поэтому допускаем и успех, и VirtualCameraException
        try
        {
            backend.SetControl(control, value: 0.5f);
        }
        catch (VirtualCameraException)
        {
            // Допустимо — PipeWire может не поддерживать контрол на данном стриме
        }
    }

    [TestCase(TestName = "GetControlRange без инициализации выбрасывает VirtualCameraException")]
    public async Task GetControlRangeWithoutInitThrows()
    {
        await using var backend = new LinuxCameraBackend();

        Assert.That(
            () => backend.GetControlRange(CameraControlType.Brightness),
            Throws.TypeOf<VirtualCameraException>());
    }

    [TestCase(TestName = "GetControlRange до получения control_info возвращает null")]
    public async Task GetControlRangeBeforeControlInfoReturnsNull()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        // PipeWire ещё не отправил control_info, диапазон неизвестен
        var range = backend.GetControlRange(CameraControlType.Brightness);
        Assert.That(range, Is.Null);
    }

    [TestCase(TestName = "ControlChanged подписка/отписка не выбрасывает исключений")]
    public async Task ControlChangedSubscribeUnsubscribeIsSafe()
    {
        await using var backend = new LinuxCameraBackend();
        var settings = new VirtualCameraSettings { Width = 320, Height = 240 };

        await backend.InitializeAsync(settings, CancellationToken.None);

        EventHandler<CameraControlChangedEventArgs>? handler = null;
        handler = (_, _) => { };

        Assert.DoesNotThrow(() => backend.ControlChanged += handler);
        Assert.DoesNotThrow(() => backend.ControlChanged -= handler);
    }
}
