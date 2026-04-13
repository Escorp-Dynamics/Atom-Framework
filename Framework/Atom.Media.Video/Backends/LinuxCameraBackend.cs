#pragma warning disable CA1308, CS0067, MA0051, MA0003

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atom.Media.Video.Backends.PipeWire;
using static Atom.Media.Video.Backends.PipeWire.PipeWireNative;

namespace Atom.Media.Video.Backends;

[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxCameraBackend : IVirtualCameraBackend
{
    private int cleanupState;
    private IntPtr threadLoop;
    private IntPtr context;
    private IntPtr core;
    private SpaHook* coreListener;
    private PwCoreEvents* coreEvents;

    private readonly Lock frameLock = new();
    private readonly ManualResetEventSlim coreSyncCompleted = new(false);
    private GCHandle selfHandle;
    private PipeWireExportedVideoDevice? exportedDevice;
    private PipeWireExportedVideoSourceNode? exportedNode;
    private byte[]? latestFrame;
    private bool isCapturing;
    private string? coreError;
    private int expectedCoreSyncSeq = int.MinValue;
    private int lastCompletedCoreSyncSeq = int.MinValue;

    public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

    public string DeviceIdentifier { get; private set; } = string.Empty;

    public bool IsCapturing => Volatile.Read(ref isCapturing);

    private static readonly VirtualCameraException? pipeWireInitError = TryInitPipeWire();

    private static VirtualCameraException? TryInitPipeWire()
    {
        try
        {
            pw_init(IntPtr.Zero, IntPtr.Zero);
            return null;
        }
        catch (DllNotFoundException ex)
        {
            return new VirtualCameraException(
                "libpipewire-0.3 не найден. Установите PipeWire:" + Environment.NewLine +
                "  Fedora: sudo dnf install pipewire-devel" + Environment.NewLine +
                "  Ubuntu: sudo apt install libpipewire-0.3-dev" + Environment.NewLine +
                "  Arch: sudo pacman -S pipewire", ex);
        }
    }

    public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pipeWireInitError is not null)
        {
            throw new VirtualCameraException(pipeWireInitError.Message, pipeWireInitError);
        }

        PrepareFrameBuffer(settings);

        threadLoop = pw_thread_loop_new("atom-vcam", IntPtr.Zero);
        if (threadLoop == IntPtr.Zero)
        {
            throw new VirtualCameraException("Не удалось создать PipeWire thread loop.");
        }

        var loop = pw_thread_loop_get_loop(threadLoop);
        context = pw_context_new(loop, IntPtr.Zero, 0);
        if (context == IntPtr.Zero)
        {
            pw_thread_loop_destroy(threadLoop);
            threadLoop = IntPtr.Zero;
            throw new VirtualCameraException("Не удалось создать PipeWire context.");
        }

        if (pw_thread_loop_start(threadLoop) < 0)
        {
            pw_context_destroy(context);
            pw_thread_loop_destroy(threadLoop);
            context = IntPtr.Zero;
            threadLoop = IntPtr.Zero;
            throw new VirtualCameraException("Не удалось запустить PipeWire thread loop.");
        }

        pw_thread_loop_lock(threadLoop);
        try
        {
            core = pw_context_connect(context, IntPtr.Zero, 0);
            if (core == IntPtr.Zero)
            {
                throw new VirtualCameraException(
                    "Не удалось подключиться к PipeWire daemon. Убедитесь, что PipeWire запущен.");
            }

            selfHandle = GCHandle.Alloc(this);
            RegisterCoreListener();

            exportedDevice = new PipeWireExportedVideoDevice();
            exportedDevice.Export(core, CreateDeviceProperties(settings));

            var deviceBoundId = WaitForCoreSyncAndGetDeviceBoundId();
            var properties = CreateNodeProperties(settings, deviceBoundId);
            exportedNode = new PipeWireExportedVideoSourceNode(settings, GetLatestFrameSnapshot);
            exportedNode.Export(core, properties);

            var nodeBoundId = WaitForCoreSync();
            if (nodeBoundId != PW_ID_ANY)
            {
                exportedDevice.BindManagedObject(nodeBoundId, SPA_TYPE_INTERFACE_Node, properties);
            }
        }
        catch
        {
            pw_thread_loop_unlock(threadLoop);
            CleanupNativeResources();
            throw;
        }

        pw_thread_loop_unlock(threadLoop);

        DeviceIdentifier = "pipewire:" + settings.Name;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exportedNode is null)
        {
            throw new VirtualCameraException("PipeWire backend не инициализирован.");
        }

        ThrowIfCoreError();
        exportedNode.Start();
        Volatile.Write(ref isCapturing, true);
        return ValueTask.CompletedTask;
    }

    public void WriteFrame(ReadOnlySpan<byte> frameData)
    {
        ThrowIfCoreError();

        if (!Volatile.Read(ref isCapturing))
        {
            throw new VirtualCameraException("Захват не запущен.");
        }

        lock (frameLock)
        {
            if (latestFrame is null || latestFrame.Length < frameData.Length)
            {
                latestFrame = GC.AllocateUninitializedArray<byte>(frameData.Length);
            }

            frameData.CopyTo(latestFrame);
        }
    }

    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Volatile.Write(ref isCapturing, false);
        exportedNode?.Stop();
        ThrowIfCoreError();
        return ValueTask.CompletedTask;
    }

    public void SetControl(CameraControlType control, float value)
    {
        if (exportedNode is null)
        {
            throw new VirtualCameraException("PipeWire backend не инициализирован.");
        }

        exportedNode.ThrowControlsNotSupported();
    }

    public float GetControl(CameraControlType control)
    {
        if (exportedNode is null)
        {
            throw new VirtualCameraException("PipeWire backend не инициализирован.");
        }

        exportedNode.ThrowControlsNotSupported();
        return 0;
    }

    public CameraControlRange? GetControlRange(CameraControlType control)
    {
        if (exportedNode is null)
        {
            throw new VirtualCameraException("PipeWire backend не инициализирован.");
        }

        return exportedNode.GetControlRange(control);
    }

    public ValueTask DisposeAsync()
    {
        CleanupNativeResources();
        return ValueTask.CompletedTask;
    }

    private void RegisterCoreListener()
    {
        coreEvents = (PwCoreEvents*)NativeMemory.AllocZeroed((nuint)sizeof(PwCoreEvents));
        coreEvents->Version = 0;
        coreEvents->Done = &OnCoreDone;
        coreEvents->Error = &OnCoreError;

        coreListener = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));
        pw_core_add_listener(core, coreListener, coreEvents, (void*)GCHandle.ToIntPtr(selfHandle));
    }

    private void PrepareFrameBuffer(VirtualCameraSettings settings)
    {
        var expectedFrameSize = settings.PixelFormat.CalculateFrameSize(settings.Width, settings.Height);
        if (expectedFrameSize <= 0)
        {
            throw new VirtualCameraException("Некорректный формат или размер кадра для виртуальной камеры.");
        }

        latestFrame = GC.AllocateUninitializedArray<byte>(expectedFrameSize);
    }

    private byte[] GetLatestFrameSnapshot()
    {
        lock (frameLock)
        {
            if (latestFrame is null)
            {
                throw new VirtualCameraException("Буфер кадра не инициализирован.");
            }

            return latestFrame;
        }
    }

    private static IntPtr CreateNodeProperties(VirtualCameraSettings cameraSettings, uint deviceBoundId)
    {
        var props = pw_properties_new(IntPtr.Zero);
        var deviceName = BuildDeviceName(cameraSettings);
        var nodeName = BuildNodeName(cameraSettings);
        _ = pw_properties_set(props, "media.type", "Video");
        _ = pw_properties_set(props, "media.category", "Source");
        _ = pw_properties_set(props, "media.role", "Camera");
        _ = pw_properties_set(props, "media.class", "Video/Source");
        _ = pw_properties_set(props, "media.name", cameraSettings.Name);
        _ = pw_properties_set(props, "device.api", "pipewire");
        _ = pw_properties_set(props, "device.name", deviceName);
        _ = pw_properties_set(props, "node.name", nodeName);
        _ = pw_properties_set(props, "node.nick", cameraSettings.Name);
        _ = pw_properties_set(props, "node.description", cameraSettings.Name);
        _ = pw_properties_set(props, "node.virtual", "true");
        _ = pw_properties_set(props, "factory.mode", "split");
        _ = pw_properties_set(props, "object.register", "true");

        SetOptionalProperty(props, "device.vendor.name", cameraSettings.Vendor);
        SetOptionalProperty(props, "device.product.name", cameraSettings.Model);
        SetOptionalProperty(props, "device.serial", cameraSettings.SerialNumber);
        SetOptionalProperty(props, "device.description", cameraSettings.Description ?? cameraSettings.Name);
        SetOptionalProperty(props, "device.firmware.version", cameraSettings.FirmwareVersion);
        SetOptionalProperty(props, "device.bus", cameraSettings.BusType ?? "virtual");
        SetOptionalProperty(props, "device.form-factor", cameraSettings.FormFactor ?? "webcam");
        SetOptionalProperty(props, "device.icon-name", cameraSettings.IconName ?? "camera-web");
        SetOptionalProperty(props, "device.id", cameraSettings.DeviceId);

        if (deviceBoundId != PW_ID_ANY)
        {
            _ = pw_properties_set(props, "device.id", deviceBoundId.ToString(CultureInfo.InvariantCulture));
        }

        if (cameraSettings.DeviceId is not null)
        {
            _ = pw_properties_set(props, "node.group", cameraSettings.DeviceId);
        }

        if (cameraSettings.UsbVendorId is { } vid)
        {
            _ = pw_properties_set(props, "device.vendor.id", FormatUsbId(vid));
        }

        if (cameraSettings.UsbProductId is { } pid)
        {
            _ = pw_properties_set(props, "device.product.id", FormatUsbId(pid));
        }

        if (cameraSettings.ExtraProperties is { } extras)
        {
            foreach (var (key, value) in extras)
            {
                _ = pw_properties_set(props, key, value);
            }
        }

        return props;
    }

    private static IntPtr CreateDeviceProperties(VirtualCameraSettings cameraSettings)
    {
        var props = pw_properties_new(IntPtr.Zero);
        var deviceName = BuildDeviceName(cameraSettings);
        _ = pw_properties_set(props, "device.api", "pipewire");
        _ = pw_properties_set(props, "device.class", "camera");
        _ = pw_properties_set(props, "media.class", "Video/Device");
        _ = pw_properties_set(props, "media.name", cameraSettings.Name);
        _ = pw_properties_set(props, "node.virtual", "true");
        _ = pw_properties_set(props, "factory.mode", "split");
        _ = pw_properties_set(props, "object.register", "true");
        _ = pw_properties_set(props, "device.name", deviceName);
        _ = pw_properties_set(props, "device.nick", cameraSettings.Name);

        SetOptionalProperty(props, "device.vendor.name", cameraSettings.Vendor);
        SetOptionalProperty(props, "device.product.name", cameraSettings.Model);
        SetOptionalProperty(props, "device.serial", cameraSettings.SerialNumber);
        SetOptionalProperty(props, "device.description", cameraSettings.Description ?? cameraSettings.Name);
        SetOptionalProperty(props, "device.firmware.version", cameraSettings.FirmwareVersion);
        SetOptionalProperty(props, "device.bus", cameraSettings.BusType ?? "virtual");
        SetOptionalProperty(props, "device.form-factor", cameraSettings.FormFactor ?? "webcam");
        SetOptionalProperty(props, "device.icon-name", cameraSettings.IconName ?? "camera-web");

        if (cameraSettings.DeviceId is not null)
        {
            _ = pw_properties_set(props, "device.id", cameraSettings.DeviceId);
            _ = pw_properties_set(props, "node.group", cameraSettings.DeviceId);
        }

        if (cameraSettings.UsbVendorId is { } vid)
        {
            _ = pw_properties_set(props, "device.vendor.id", FormatUsbId(vid));
        }

        if (cameraSettings.UsbProductId is { } pid)
        {
            _ = pw_properties_set(props, "device.product.id", FormatUsbId(pid));
        }

        if (cameraSettings.ExtraProperties is { } extras)
        {
            foreach (var (key, value) in extras)
            {
                if (key.StartsWith("device.", StringComparison.Ordinal) || key.StartsWith("object.", StringComparison.Ordinal))
                {
                    _ = pw_properties_set(props, key, value);
                }
            }
        }

        return props;
    }

    private static void SetOptionalProperty(IntPtr props, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = pw_properties_set(props, key, value);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCoreDone(void* data, uint id, int seq)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated)
        {
            return;
        }

        var self = (LinuxCameraBackend)handle.Target!;
        Volatile.Write(ref self.lastCompletedCoreSyncSeq, seq);

        if (Volatile.Read(ref self.expectedCoreSyncSeq) == seq)
        {
            self.coreSyncCompleted.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCoreError(void* data, uint id, int seq, int res, byte* message)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated)
        {
            return;
        }

        var self = (LinuxCameraBackend)handle.Target!;
        var errorText = message is not null
            ? Marshal.PtrToStringUTF8((IntPtr)message) ?? "Unknown PipeWire daemon error"
            : "PipeWire daemon error (res=" + res.ToString(CultureInfo.InvariantCulture) + ")";

        self.coreError ??= errorText;
        Volatile.Write(ref self.isCapturing, false);
    }

    private void CleanupNativeResources()
    {
        if (Interlocked.Exchange(ref cleanupState, value: 1) != 0)
            return;

        Volatile.Write(ref isCapturing, false);

        var device = exportedDevice;
        var node = exportedNode;
        exportedDevice = null;
        exportedNode = null;

        try
        {
            CleanupPipeWireObjects(device, node);
        }
        finally
        {
            device?.Dispose();
            node?.Dispose();

            FreeNativeListeners();

            if (selfHandle.IsAllocated)
            {
                selfHandle.Free();
            }

            coreSyncCompleted.Dispose();
            latestFrame = null;
            coreError = null;
            Volatile.Write(ref expectedCoreSyncSeq, int.MinValue);
            Volatile.Write(ref lastCompletedCoreSyncSeq, int.MinValue);
        }
    }

    private void CleanupPipeWireObjects(
        PipeWireExportedVideoDevice? device,
        PipeWireExportedVideoSourceNode? node)
    {
        var loop = threadLoop;
        var currentCore = core;
        var currentContext = context;

        threadLoop = IntPtr.Zero;
        core = IntPtr.Zero;
        context = IntPtr.Zero;

        if (loop == IntPtr.Zero) return;

        pw_thread_loop_lock(loop);
        try
        {
            device?.DestroyExportProxy();
            node?.DestroyExportProxy();

            if (currentCore != IntPtr.Zero)
            {
                _ = pw_core_disconnect(currentCore);
            }
        }
        finally
        {
            pw_thread_loop_unlock(loop);
        }

        pw_thread_loop_stop(loop);

        if (currentContext != IntPtr.Zero)
        {
            pw_context_destroy(currentContext);
        }

        pw_thread_loop_destroy(loop);
    }

    private uint WaitForCoreSyncAndGetDeviceBoundId()
    {
        _ = WaitForCoreSync();
        return exportedDevice?.TryGetBoundId() ?? PW_ID_ANY;
    }

    private uint WaitForCoreSync()
    {
        coreSyncCompleted.Reset();
        var seq = pw_core_sync(core, PW_ID_ANY, 0);
        if (seq < 0)
        {
            return PW_ID_ANY;
        }

        Volatile.Write(ref expectedCoreSyncSeq, seq);
        if (Volatile.Read(ref lastCompletedCoreSyncSeq) != seq)
        {
            pw_thread_loop_unlock(threadLoop);
            try
            {
                _ = coreSyncCompleted.Wait(TimeSpan.FromMilliseconds(500));
            }
            finally
            {
                pw_thread_loop_lock(threadLoop);
            }
        }

        Volatile.Write(ref expectedCoreSyncSeq, int.MinValue);
        return exportedNode?.TryGetBoundId() ?? exportedDevice?.TryGetBoundId() ?? PW_ID_ANY;
    }

    private static string BuildDeviceName(VirtualCameraSettings cameraSettings) =>
        "atom.device." + BuildMetadataSlug(cameraSettings.DeviceId ?? cameraSettings.Name, "virtual-camera");

    private static string BuildNodeName(VirtualCameraSettings cameraSettings) =>
        "atom.camera." + BuildMetadataSlug(cameraSettings.DeviceId ?? cameraSettings.Name, "virtual-camera");

    private static string BuildMetadataSlug(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value!;
        Span<char> buffer = stackalloc char[source.Length];
        var length = 0;
        var previousWasSeparator = false;

        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (length > 0 && !previousWasSeparator)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        var slug = new string(buffer[..length]).Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }

    private static string FormatUsbId(int value) => "0x" + value.ToString("x4", CultureInfo.InvariantCulture);

    private void FreeNativeListeners()
    {
        if (coreListener is not null)
        {
            NativeMemory.Free(coreListener);
            coreListener = null;
        }

        if (coreEvents is not null)
        {
            NativeMemory.Free(coreEvents);
            coreEvents = null;
        }
    }

    private void ThrowIfCoreError()
    {
        if (coreError is not null)
        {
            throw new VirtualCameraException("PipeWire error: " + coreError);
        }
    }

    internal static int CalculateStride(int width, VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => width * 3,
        VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32
            or VideoPixelFormat.Argb32 or VideoPixelFormat.Abgr32 => width * 4,
        VideoPixelFormat.Yuv420P or VideoPixelFormat.Yuv422P
            or VideoPixelFormat.Yuv444P => width,
        VideoPixelFormat.Yuv420P10Le or VideoPixelFormat.Yuv422P10Le
            or VideoPixelFormat.Yuv444P10Le => width * 2,
        VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 => width,
        VideoPixelFormat.P010Le => width * 2,
        VideoPixelFormat.Yuyv422 or VideoPixelFormat.Uyvy422 => width * 2,
        VideoPixelFormat.Gray8 => width,
        VideoPixelFormat.Gray16Le => width * 2,
        VideoPixelFormat.Mjpeg or VideoPixelFormat.H264
            or VideoPixelFormat.Vp8 or VideoPixelFormat.Vp9 => 0,
        _ => width,
    };

    internal static uint MapControlToSpaProp(CameraControlType control) => control switch
    {
        CameraControlType.Brightness => SPA_PROP_brightness,
        CameraControlType.Contrast => SPA_PROP_contrast,
        CameraControlType.Saturation => SPA_PROP_saturation,
        CameraControlType.Hue => SPA_PROP_hue,
        CameraControlType.Gamma => SPA_PROP_gamma,
        CameraControlType.Exposure => SPA_PROP_exposure,
        CameraControlType.Gain => SPA_PROP_gain,
        CameraControlType.Sharpness => SPA_PROP_sharpness,
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Неизвестный тип контрола камеры."),
    };

    internal static bool TryMapSpaPropToControl(uint propId, out CameraControlType control)
    {
        control = propId switch
        {
            SPA_PROP_brightness => CameraControlType.Brightness,
            SPA_PROP_contrast => CameraControlType.Contrast,
            SPA_PROP_saturation => CameraControlType.Saturation,
            SPA_PROP_hue => CameraControlType.Hue,
            SPA_PROP_gamma => CameraControlType.Gamma,
            SPA_PROP_exposure => CameraControlType.Exposure,
            SPA_PROP_gain => CameraControlType.Gain,
            SPA_PROP_sharpness => CameraControlType.Sharpness,
            _ => default,
        };

        return propId is >= SPA_PROP_brightness and <= SPA_PROP_sharpness;
    }
}
