#pragma warning disable CA1308, S1450

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atom.Media.Audio.Backends.PipeWire;
using static Atom.Media.Audio.Backends.PipeWire.PipeWireAudioNative;

namespace Atom.Media.Audio.Backends;

/// <summary>
/// Бэкенд виртуального микрофона для Linux через нативный PipeWire API.
/// Создаёт PipeWire audio source ноду, доступную как микрофон.
/// Root-права не требуются.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxMicrophoneBackend : IVirtualMicrophoneBackend
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

    private byte[]? pendingBuffer;
    private int pendingBufferSize;
    private int frameStride;
    private readonly Lock frameLock = new();
    private bool isCapturing;
    private volatile string? streamError;
    private readonly Dictionary<MicrophoneControlType, MicrophoneControlRange> controlRanges = [];
    private readonly Dictionary<MicrophoneControlType, float> controlValues = new()
    {
        [MicrophoneControlType.Volume] = 1.0f,
        [MicrophoneControlType.Mute] = 0.0f,
    };

    /// <inheritdoc/>
    public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool IsCapturing => Volatile.Read(ref isCapturing);

    private static readonly VirtualMicrophoneException? pipeWireInitError = TryInitPipeWire();

    private static VirtualMicrophoneException? TryInitPipeWire()
    {
        try
        {
            pw_init(IntPtr.Zero, IntPtr.Zero);
            return null;
        }
        catch (DllNotFoundException ex)
        {
            return new VirtualMicrophoneException(
                "libpipewire-0.3 не найден. Установите PipeWire:" + Environment.NewLine +
                "  Fedora: sudo dnf install pipewire-devel" + Environment.NewLine +
                "  Ubuntu: sudo apt install libpipewire-0.3-dev" + Environment.NewLine +
                "  Arch: sudo pacman -S pipewire", ex);
        }
    }

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
    {
        if (pipeWireInitError is not null)
        {
            throw new VirtualMicrophoneException(pipeWireInitError.Message, pipeWireInitError);
        }

        PrepareAudioBuffer(settings);
        InitThreadLoop();

        pw_thread_loop_lock(threadLoop);
        try
        {
            ConnectCore();
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
            throw new VirtualMicrophoneException("Стрим не инициализирован.");
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
    public void WriteSamples(ReadOnlySpan<byte> sampleData)
    {
        if (!Volatile.Read(ref isCapturing) || streamError is not null) return;

        lock (frameLock)
        {
            if (pendingBuffer is null || pendingBuffer.Length < sampleData.Length)
            {
                if (pendingBuffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(pendingBuffer);
                }

                pendingBuffer = ArrayPool<byte>.Shared.Rent(sampleData.Length);
            }

            sampleData.CopyTo(pendingBuffer);
            pendingBufferSize = sampleData.Length;
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

    private void PrepareAudioBuffer(VirtualMicrophoneSettings settings)
    {
        frameStride = settings.SampleFormat.GetBytesPerSample() * settings.Channels;

        var latencyFrames = settings.SampleRate * settings.LatencyMs / 1000;
        var bufferSize = settings.SampleFormat.CalculateBufferSize(
            latencyFrames, settings.Channels);
        if (bufferSize > 0)
        {
            pendingBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        }
    }

    private void InitThreadLoop()
    {
        threadLoop = pw_thread_loop_new("atom-vmic", IntPtr.Zero);
        if (threadLoop == IntPtr.Zero)
            throw new VirtualMicrophoneException("Не удалось создать PipeWire thread loop.");

        var loop = pw_thread_loop_get_loop(threadLoop);

        context = pw_context_new(loop, IntPtr.Zero, 0);
        if (context == IntPtr.Zero)
        {
            pw_thread_loop_destroy(threadLoop);
            threadLoop = IntPtr.Zero;
            throw new VirtualMicrophoneException("Не удалось создать PipeWire context.");
        }

        if (pw_thread_loop_start(threadLoop) < 0)
        {
            pw_context_destroy(context);
            pw_thread_loop_destroy(threadLoop);
            context = IntPtr.Zero;
            threadLoop = IntPtr.Zero;
            throw new VirtualMicrophoneException("Не удалось запустить PipeWire thread loop.");
        }
    }

    private void ConnectCore()
    {
        core = pw_context_connect(context, IntPtr.Zero, 0);
        if (core == IntPtr.Zero)
        {
            throw new VirtualMicrophoneException(
                "Не удалось подключиться к PipeWire daemon. Убедитесь, что PipeWire запущен.");
        }
    }

    private void RegisterCoreListener()
    {
        coreEvents = (PwCoreEvents*)NativeMemory.AllocZeroed((nuint)sizeof(PwCoreEvents));
        coreEvents->Version = 0;
        coreEvents->Error = &OnCoreError;

        coreListener = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));

        pw_core_add_listener(core, coreListener, coreEvents, (void*)GCHandle.ToIntPtr(selfHandle));
    }

    private static IntPtr CreateStreamProperties(VirtualMicrophoneSettings micSettings)
    {
        var props = pw_properties_new(IntPtr.Zero);
        var deviceName = BuildDeviceName(micSettings);
        var nodeName = BuildNodeName(micSettings);
        _ = pw_properties_set(props, "media.type", "Audio");
        _ = pw_properties_set(props, "media.category", "Source");
        _ = pw_properties_set(props, "media.role", "Communication");
        _ = pw_properties_set(props, "media.class", "Audio/Source");
        _ = pw_properties_set(props, "media.name", micSettings.Name);
        _ = pw_properties_set(props, "device.name", deviceName);
        _ = pw_properties_set(props, "node.name", nodeName);
        _ = pw_properties_set(props, "node.nick", micSettings.Name);
        _ = pw_properties_set(props, "node.description", micSettings.Name);
        _ = pw_properties_set(props, "node.virtual", "true");

        var latencyFrames = micSettings.SampleRate * micSettings.LatencyMs / 1000;
        var latencyValue = latencyFrames.ToString(CultureInfo.InvariantCulture)
            + "/" + micSettings.SampleRate.ToString(CultureInfo.InvariantCulture);
        _ = pw_properties_set(props, "node.latency", latencyValue);

        SetOptionalProperty(props, "device.vendor.name", micSettings.Vendor);
        SetOptionalProperty(props, "device.product.name", micSettings.Model);
        SetOptionalProperty(props, "device.serial", micSettings.SerialNumber);
        SetOptionalProperty(props, "device.description", micSettings.Description ?? micSettings.Name);
        SetOptionalProperty(props, "device.id", micSettings.DeviceId);

        if (micSettings.UsbVendorId is { } vid)
        {
            _ = pw_properties_set(props, "device.vendor.id", FormatUsbId(vid));
        }

        if (micSettings.UsbProductId is { } pid)
        {
            _ = pw_properties_set(props, "device.product.id", FormatUsbId(pid));
        }

        if (micSettings.DeviceId is not null)
        {
            _ = pw_properties_set(props, "node.group", micSettings.DeviceId);
        }

        if (micSettings.ExtraProperties is { } extras)
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
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = pw_properties_set(props, key, value);
        }
    }

    private static string BuildDeviceName(VirtualMicrophoneSettings micSettings) =>
        "atom.device." + BuildMetadataSlug(micSettings.DeviceId ?? micSettings.Name, "virtual-microphone");

    private static string BuildNodeName(VirtualMicrophoneSettings micSettings) =>
        "atom.microphone." + BuildMetadataSlug(micSettings.DeviceId ?? micSettings.Name, "virtual-microphone");

    private static string FormatUsbId(int value) => "0x" + value.ToString("x4", CultureInfo.InvariantCulture);

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

    private void CreateStream(VirtualMicrophoneSettings micSettings)
    {
        var props = CreateStreamProperties(micSettings);

        stream = pw_stream_new(core, micSettings.Name, props);

        if (stream == IntPtr.Zero)
        {
            throw new VirtualMicrophoneException("Не удалось создать PipeWire stream.");
        }

        RegisterStreamListener();
        ConnectStream(micSettings);
        ApplyDefaultControls();
    }

    private void RegisterStreamListener()
    {
        streamEvents = (PwStreamEvents*)NativeMemory.AllocZeroed((nuint)sizeof(PwStreamEvents));
        streamEvents->Version = PW_VERSION_STREAM_EVENTS;
        streamEvents->Process = &OnProcess;
        streamEvents->StateChanged = &OnStateChanged;
        streamEvents->ControlInfo = &OnControlInfo;

        streamListener = (SpaHook*)NativeMemory.AllocZeroed((nuint)sizeof(SpaHook));

        pw_stream_add_listener(stream, streamListener, streamEvents, (void*)GCHandle.ToIntPtr(selfHandle));
    }

    private void ConnectStream(VirtualMicrophoneSettings micSettings)
    {
        Span<byte> podBuffer = stackalloc byte[256];
        var podSize = SpaAudioPodBuilder.BuildAudioFormatPod(
            podBuffer,
            micSettings.SampleRate,
            micSettings.Channels,
            micSettings.SampleFormat);

        fixed (byte* podBytes = podBuffer[..podSize])
        {
            var podPtr = (IntPtr)podBytes;
            var result = pw_stream_connect(
                stream,
                PW_DIRECTION_OUTPUT,
                PW_ID_ANY,
                PW_STREAM_FLAG_AUTOCONNECT |
                PW_STREAM_FLAG_INACTIVE |
                PW_STREAM_FLAG_MAP_BUFFERS |
                PW_STREAM_FLAG_RT_PROCESS,
                &podPtr,
                1);

            if (result < 0)
            {
                throw new VirtualMicrophoneException(
                    "Не удалось подключить PipeWire stream (код: " +
                    result.ToString(CultureInfo.InvariantCulture) + ").");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCoreError(void* data, uint id, int seq, int res, byte* message)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)data);
        if (!handle.IsAllocated) return;

        var self = (LinuxMicrophoneBackend)handle.Target!;

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

        var self = (LinuxMicrophoneBackend)handle.Target!;

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

        var self = (LinuxMicrophoneBackend)handle.Target!;

        if (!TryMapSpaPropToControl(id, out var controlType)) return;

        var range = new MicrophoneControlRange(control->Min, control->Max, control->Default);
        lock (self.controlRanges)
        {
            self.controlRanges[controlType] = range;
        }

        if (control->NValues > 0 && control->Values is not null)
        {
            lock (self.controlValues)
            {
                self.controlValues[controlType] = *control->Values;
            }

            self.RaiseControlChanged(controlType, *control->Values, range);
        }
    }

    private void RaiseControlChanged(
        MicrophoneControlType controlType, float value, MicrophoneControlRange range)
    {
        ControlChanged?.Invoke(this, new MicrophoneControlChangedEventArgs
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

        var self = (LinuxMicrophoneBackend)handle.Target!;
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
                    WriteChunkData(buf, d);
                }
            }
        }

        _ = pw_stream_queue_buffer(stream, buf);
    }

    private void WriteChunkData(PwBuffer* buffer, SpaData* d)
    {
        if (d->Data is null || d->Chunk is null)
        {
            return;
        }

        var maxBufferBytes = (int)d->MaxSize;
        var requestedFrames = buffer is not null ? (int)buffer->Requested : 0;
        var requestedBytes = requestedFrames > 0 && frameStride > 0
            ? requestedFrames * frameStride
            : maxBufferBytes;
        var targetBytes = Math.Min(maxBufferBytes, requestedBytes);

        if (targetBytes <= 0)
        {
            d->Chunk->Offset = 0;
            d->Chunk->Size = 0;
            d->Chunk->Stride = frameStride;
            return;
        }

        var target = new Span<byte>(d->Data, targetBytes);

        if (pendingBuffer is not null && pendingBufferSize > 0)
        {
            var copySize = Math.Min(pendingBufferSize, target.Length);
            pendingBuffer.AsSpan(0, copySize).CopyTo(target);

            if (copySize < target.Length)
            {
                target[copySize..].Clear();
            }

            d->Chunk->Offset = 0;
            d->Chunk->Size = (uint)copySize;
            d->Chunk->Stride = frameStride;
            return;
        }

        target.Clear();
        d->Chunk->Offset = 0;
        d->Chunk->Size = (uint)target.Length;
        d->Chunk->Stride = frameStride;
    }

    private void CleanupNativeResources()
    {
        CleanupPipeWireObjects();
        FreeNativeListeners();

        if (selfHandle.IsAllocated)
        {
            selfHandle.Free();
        }

        if (pendingBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(pendingBuffer);
            pendingBuffer = null;
        }
    }

    private void CleanupPipeWireObjects()
    {
        if (threadLoop == IntPtr.Zero) return;

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
            throw new VirtualMicrophoneException("PipeWire stream error: " + error);
        }
    }

    private void ApplyDefaultControls()
    {
        pw_thread_loop_lock(threadLoop);
        try
        {
            foreach (var (control, value) in controlValues)
            {
                var propId = MapControlToSpaProp(control);
                var v = value;
                _ = pw_stream_set_control(stream, propId, nValues: 1, &v);
            }
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }
    }

    /// <inheritdoc/>
    public void SetControl(MicrophoneControlType control, float value)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualMicrophoneException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        lock (controlValues)
        {
            controlValues[control] = value;
        }

        var propId = MapControlToSpaProp(control);

        pw_thread_loop_lock(threadLoop);
        try
        {
            // Best-effort: PipeWire может не принять контрол для source ноды
            _ = pw_stream_set_control(stream, propId, nValues: 1, &value);
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }
    }

    /// <inheritdoc/>
    public float GetControl(MicrophoneControlType control)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualMicrophoneException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        var propId = MapControlToSpaProp(control);

        pw_thread_loop_lock(threadLoop);
        try
        {
            var ctrl = pw_stream_get_control(stream, propId);
            if (ctrl is not null && ctrl->NValues > 0 && ctrl->Values is not null)
            {
                return *ctrl->Values;
            }
        }
        finally
        {
            pw_thread_loop_unlock(threadLoop);
        }

        // Fallback: PipeWire source ноды не всегда предоставляют контролы
        lock (controlValues)
        {
            return controlValues.GetValueOrDefault(control);
        }
    }

    /// <inheritdoc/>
    public MicrophoneControlRange? GetControlRange(MicrophoneControlType control)
    {
        if (stream == IntPtr.Zero)
        {
            throw new VirtualMicrophoneException("Стрим не инициализирован.");
        }

        ThrowIfStreamError();

        lock (controlRanges)
        {
            return controlRanges.GetValueOrDefault(control);
        }
    }

    internal static uint MapControlToSpaProp(MicrophoneControlType control) => control switch
    {
        MicrophoneControlType.Volume => SPA_PROP_volume,
        MicrophoneControlType.Mute => SPA_PROP_mute,
        _ => throw new ArgumentOutOfRangeException(
            nameof(control), control, "Неизвестный тип контрола микрофона."),
    };

    internal static bool TryMapSpaPropToControl(uint propId, out MicrophoneControlType control)
    {
        control = propId switch
        {
            SPA_PROP_volume => MicrophoneControlType.Volume,
            SPA_PROP_mute => MicrophoneControlType.Mute,
            _ => default,
        };

        return propId is SPA_PROP_volume or SPA_PROP_mute;
    }
}
