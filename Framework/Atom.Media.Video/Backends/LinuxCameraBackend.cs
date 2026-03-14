#pragma warning disable CA1308, S1450

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atom.Media.Video.Backends.PipeWire;
using static Atom.Media.Video.Backends.PipeWire.PipeWireNative;

namespace Atom.Media.Video.Backends;

/// <summary>
/// Бэкенд виртуальной камеры для Linux через нативный PipeWire API.
/// Создаёт PipeWire video source ноду, доступную как камера.
/// Root-права не требуются.
/// </summary>
/// <remarks>
/// Требования:
/// <list type="bullet">
///   <item><c>libpipewire-0.3</c> должен быть установлен.</item>
///   <item>PipeWire daemon должен быть запущен.</item>
/// </list>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxCameraBackend : IVirtualCameraBackend
{
    private IntPtr threadLoop;
    private IntPtr context;
    private IntPtr core;
    private IntPtr stream;
    private SpaHook* streamListener;
    private PwStreamEvents* streamEvents;
    private SpaHook* coreListener;
    private PwCoreEvents* coreEvents;
    private GCHandle selfHandle;

    private byte[]? pendingFrame;
    private int pendingFrameSize;
    private int frameStride;
    private readonly Lock frameLock = new();
    private volatile bool hasNewFrame;
    private bool isCapturing;
    private volatile string? streamError;
    private readonly Dictionary<CameraControlType, CameraControlRange> controlRanges = [];

    /// <inheritdoc/>
    public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
    {
        if (pipeWireInitError is not null)
        {
            throw new VirtualCameraException(pipeWireInitError.Message, pipeWireInitError);
        }

        PrepareFrameBuffer(settings);

        threadLoop = pw_thread_loop_new("atom-vcam", IntPtr.Zero);
        if (threadLoop == IntPtr.Zero)
            throw new VirtualCameraException("Не удалось создать PipeWire thread loop.");

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
            CreateStream(settings);
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

    /// <inheritdoc/>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualCameraException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        pw_thread_loop_lock(threadLoop);

        try
        {
            _ = pw_stream_set_active(stream, active: true);
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }

        Volatile.Write(ref isCapturing, value: true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void WriteFrame(ReadOnlySpan<byte> frameData)
    {
        if (!Volatile.Read(ref isCapturing) || streamError is not null) return;

        lock (frameLock)
        {
            if (pendingFrame is null || pendingFrame.Length < frameData.Length)
            {
                if (pendingFrame is not null)
                {
                    ArrayPool<byte>.Shared.Return(pendingFrame);
                }

                pendingFrame = ArrayPool<byte>.Shared.Rent(frameData.Length);
            }

            frameData.CopyTo(pendingFrame);
            pendingFrameSize = frameData.Length;
            hasNewFrame = true;
        }
    }

    /// <inheritdoc/>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref isCapturing, value: false);

        if (stream == IntPtr.Zero)
        {
            return ValueTask.CompletedTask;
        }

        pw_thread_loop_lock(threadLoop);

        try
        {
            _ = pw_stream_set_active(stream, active: false);
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }

        ThrowIfStreamError();

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        CleanupNativeResources();
        return ValueTask.CompletedTask;
    }

    private void RegisterCoreListener()
    {
        coreEvents = (PwCoreEvents*)NativeMemory.AllocZeroed((nuint)sizeof(PwCoreEvents));
        coreEvents->Version = 0;
        coreEvents->Error = &OnCoreError;

        coreListener = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));

        pw_core_add_listener(core, coreListener, coreEvents, (void*)GCHandle.ToIntPtr(selfHandle));
    }

    private void PrepareFrameBuffer(VirtualCameraSettings settings)
    {
        frameStride = CalculateStride(settings.Width, settings.PixelFormat);

        var expectedFrameSize = settings.PixelFormat.CalculateFrameSize(settings.Width, settings.Height);
        if (expectedFrameSize > 0)
        {
            pendingFrame = ArrayPool<byte>.Shared.Rent(expectedFrameSize);
        }
    }

    private static IntPtr CreateStreamProperties(VirtualCameraSettings cameraSettings)
    {
        var props = pw_properties_new(IntPtr.Zero);
        _ = pw_properties_set(props, "media.type", "Video");
        _ = pw_properties_set(props, "media.category", "Source");
        _ = pw_properties_set(props, "media.role", "Camera");
        _ = pw_properties_set(props, "node.name", "atom-virtual-camera");
        _ = pw_properties_set(props, "node.description", cameraSettings.Name);

        SetOptionalProperty(props, "device.vendor.name", cameraSettings.Vendor);
        SetOptionalProperty(props, "device.product.name", cameraSettings.Model);
        SetOptionalProperty(props, "device.serial", cameraSettings.SerialNumber);
        SetOptionalProperty(props, "device.description", cameraSettings.Description);
        SetOptionalProperty(props, "device.firmware.version", cameraSettings.FirmwareVersion);
        SetOptionalProperty(props, "device.bus", cameraSettings.BusType);
        SetOptionalProperty(props, "device.form-factor", cameraSettings.FormFactor);
        SetOptionalProperty(props, "device.icon-name", cameraSettings.IconName);
        SetOptionalProperty(props, "device.id", cameraSettings.DeviceId);

        if (cameraSettings.DeviceId is not null)
        {
            _ = pw_properties_set(props, "node.group", cameraSettings.DeviceId);
        }

        if (cameraSettings.UsbVendorId is { } vid)
        {
            _ = pw_properties_set(props, "device.vendor.id", vid.ToString(CultureInfo.InvariantCulture));
        }

        if (cameraSettings.UsbProductId is { } pid)
        {
            _ = pw_properties_set(props, "device.product.id", pid.ToString(CultureInfo.InvariantCulture));
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

    private static void SetOptionalProperty(IntPtr props, string key, string? value)
    {
        if (value is not null)
        {
            _ = pw_properties_set(props, key, value);
        }
    }

    private void CreateStream(VirtualCameraSettings cameraSettings)
    {
        var props = CreateStreamProperties(cameraSettings);

        stream = pw_stream_new(core, cameraSettings.Name, props);

        if (stream == IntPtr.Zero)
        {
            throw new VirtualCameraException("Не удалось создать PipeWire stream.");
        }

        streamEvents = (PwStreamEvents*)NativeMemory.AllocZeroed((nuint)sizeof(PwStreamEvents));
        streamEvents->Version = PW_VERSION_STREAM_EVENTS;
        streamEvents->Process = &OnProcess;
        streamEvents->StateChanged = &OnStateChanged;
        streamEvents->ControlInfo = &OnControlInfo;

        streamListener = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));

        pw_stream_add_listener(stream, streamListener, streamEvents, (void*)GCHandle.ToIntPtr(selfHandle));

        Span<byte> podBuffer = stackalloc byte[256];
        var podSize = SpaPodBuilder.BuildVideoFormatPod(
            podBuffer,
            cameraSettings.Width,
            cameraSettings.Height,
            cameraSettings.FrameRate,
            cameraSettings.PixelFormat);

        fixed (byte* podBytes = podBuffer[..podSize])
        {
            var podPtr = (IntPtr)podBytes;
            var result = pw_stream_connect(
                stream,
                PW_DIRECTION_OUTPUT,
                PW_ID_ANY,
                PW_STREAM_FLAG_MAP_BUFFERS | PW_STREAM_FLAG_DRIVER,
                &podPtr,
                1);

            if (result < 0)
            {
                throw new VirtualCameraException(
                    "Не удалось подключить PipeWire stream (код: " + result.ToString(CultureInfo.InvariantCulture) + ").");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCoreError(void* data, uint id, int seq, int res, byte* message)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated) return;

        var self = (LinuxCameraBackend)handle.Target!;

        var errorText = message is not null
            ? Marshal.PtrToStringUTF8((IntPtr)message) ?? "Unknown PipeWire daemon error"
            : "PipeWire daemon error (res=" + res.ToString(CultureInfo.InvariantCulture) + ")";

        self.streamError ??= "PipeWire daemon: " + errorText;
        Volatile.Write(ref self.isCapturing, value: false);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStateChanged(void* data, int oldState, int newState, byte* errorMessage)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated) return;

        var self = (LinuxCameraBackend)handle.Target!;

        if (newState == PW_STREAM_STATE_ERROR)
        {
            var error = errorMessage is not null
                ? Marshal.PtrToStringUTF8((IntPtr)errorMessage) ?? "Unknown PipeWire error"
                : "PipeWire stream error";

            self.streamError = error;
            Volatile.Write(ref self.isCapturing, value: false);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnControlInfo(void* data, uint id, PwStreamControl* control)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated || control is null) return;

        var self = (LinuxCameraBackend)handle.Target!;

        if (!TryMapSpaPropToControl(id, out var controlType)) return;

        var range = new CameraControlRange(control->Min, control->Max, control->Default);
        lock (self.controlRanges)
        {
            self.controlRanges[controlType] = range;
        }

        if (control->NValues > 0 && control->Values is not null)
        {
            self.RaiseControlChanged(controlType, *control->Values, range);
        }
    }

    private void RaiseControlChanged(CameraControlType controlType, float value, CameraControlRange range)
    {
        ControlChanged?.Invoke(this, new CameraControlChangedEventArgs
        {
            Control = controlType,
            Value = value,
            Range = range,
        });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProcess(void* data)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated) return;

        var self = (LinuxCameraBackend)handle.Target!;
        self.ProcessBuffer();
    }

    private void ProcessBuffer()
    {
        if (stream == IntPtr.Zero) return;

        var buf = pw_stream_dequeue_buffer(stream);
        if (buf is null || buf->Buffer is null) return;

        if (buf->Buffer->DataCount > 0)
        {
            var d = &buf->Buffer->Datas[0];

            if (d->Data is not null)
            {
                lock (frameLock)
                {
                    if (hasNewFrame && pendingFrame is not null)
                    {
                        var copySize = Math.Min(pendingFrameSize, (int)d->MaxSize);
                        pendingFrame.AsSpan(0, copySize).CopyTo(
                            new Span<byte>(d->Data, (int)d->MaxSize));

                        if (d->Chunk is not null)
                        {
                            d->Chunk->Offset = 0;
                            d->Chunk->Size = (uint)copySize;
                            d->Chunk->Stride = frameStride;
                        }

                        hasNewFrame = false;
                    }
                    else if (d->Chunk is not null)
                    {
                        d->Chunk->Size = 0;
                    }
                }
            }
        }

        _ = pw_stream_queue_buffer(stream, buf);
    }

    private void CleanupNativeResources()
    {
        if (threadLoop != IntPtr.Zero)
        {
            pw_thread_loop_lock(threadLoop);

            if (stream != IntPtr.Zero)
            {
                _ = pw_stream_disconnect(stream);
                pw_stream_destroy(stream);
                stream = IntPtr.Zero;
            }

            if (core != IntPtr.Zero)
            {
                _ = pw_core_disconnect(core);
                core = IntPtr.Zero;
            }

            pw_thread_loop_unlock(threadLoop);
            pw_thread_loop_stop(threadLoop);

            if (context != IntPtr.Zero)
            {
                pw_context_destroy(context);
                context = IntPtr.Zero;
            }

            pw_thread_loop_destroy(threadLoop);
            threadLoop = IntPtr.Zero;
        }

        FreeNativeListeners();

        if (selfHandle.IsAllocated)
        {
            selfHandle.Free();
        }

        if (pendingFrame is not null)
        {
            ArrayPool<byte>.Shared.Return(pendingFrame);
            pendingFrame = null;
        }
    }

    private void FreeNativeListeners()
    {
        if (streamListener is not null)
        {
            NativeMemory.Free(streamListener);
            streamListener = null;
        }

        if (streamEvents is not null)
        {
            NativeMemory.Free(streamEvents);
            streamEvents = null;
        }

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

    private void ThrowIfStreamError()
    {
        var error = streamError;
        if (error is not null)
        {
            throw new VirtualCameraException("PipeWire stream error: " + error);
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

    /// <inheritdoc/>
    public void SetControl(CameraControlType control, float value)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualCameraException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        var propId = MapControlToSpaProp(control);

        pw_thread_loop_lock(threadLoop);
        try
        {
            var result = pw_stream_set_control(stream, propId, nValues: 1, &value);
            if (result < 0)
            {
                throw new VirtualCameraException(
                    "Не удалось установить контрол " + control.ToString() +
                    " (код: " + result.ToString(CultureInfo.InvariantCulture) + ").");
            }
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }
    }

    /// <inheritdoc/>
    public float GetControl(CameraControlType control)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualCameraException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        var propId = MapControlToSpaProp(control);

        pw_thread_loop_lock(threadLoop);
        try
        {
            var ctrl = pw_stream_get_control(stream, propId);
            if (ctrl is null || ctrl->NValues == 0 || ctrl->Values is null)
            {
                throw new VirtualCameraException(
                    "Не удалось получить контрол " + control.ToString() + ".");
            }

            return *ctrl->Values;
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }
    }

    /// <inheritdoc/>
    public CameraControlRange? GetControlRange(CameraControlType control)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualCameraException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        lock (controlRanges)
        {
            return controlRanges.GetValueOrDefault(control);
        }
    }

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
