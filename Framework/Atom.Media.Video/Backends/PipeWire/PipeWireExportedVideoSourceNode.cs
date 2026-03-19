#pragma warning disable S4487, CA1416, S2325, IDE0022, CA2216, S1994, IDE0004, S1905, IDE0060, CA1822, MA0038

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Atom.Media.Video.Backends.PipeWire.PipeWireNative;

namespace Atom.Media.Video.Backends.PipeWire;

[SupportedOSPlatform("linux")]
internal sealed unsafe class PipeWireExportedVideoSourceNode : IDisposable
{
    private const int MaxBuffers = 16;
    private const int DefaultAdvertisedBufferCount = 4;

    private readonly VirtualCameraSettings settings;
    private readonly int frameSize;
    private readonly int frameStride;
    private readonly Func<byte[]> frameProvider;
    private readonly Lock syncRoot = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private GCHandle selfHandle;
    private IntPtr interfaceTypeName;
    private SpaNode* node;
    private SpaNodeMethods* methods;
    private SpaParamInfo* nodeParams;
    private SpaParamInfo* portParams;
    private IntPtr exportProperties;
    private IntPtr exportProxy;

    private SpaNodeEvents* nodeEvents;
    private void* nodeEventsData;
    private SpaNodeCallbacks* nodeCallbacks;
    private void* nodeCallbacksData;
    private SpaIoBuffers* ioBuffers;

    private byte[]? currentFormatPod;
    private long frameSequence;
    private bool started;
    private bool configured;
    private bool disposed;
    private int bufferCount;
    private readonly BufferState[] buffers = new BufferState[MaxBuffers];

    private sealed class BufferState
    {
        public uint Id;
        public SpaBuffer* Buffer;
        public SpaMetaHeader* Header;
        public void* Data;
        public uint MaxSize;
        public SpaChunk* Chunk;
        public IntPtr MappedAddress;
        public nuint MappedSize;
        public bool Outstanding;
    }

    internal PipeWireExportedVideoSourceNode(VirtualCameraSettings settings, Func<byte[]> frameProvider)
    {
        this.settings = settings;
        this.frameProvider = frameProvider;
        frameSize = settings.PixelFormat.CalculateFrameSize(settings.Width, settings.Height);
        frameStride = Backends.LinuxCameraBackend.CalculateStride(settings.Width, settings.PixelFormat);

        for (var index = 0; index < buffers.Length; index++)
        {
            buffers[index] = new BufferState();
        }
    }

    internal void Export(IntPtr core, IntPtr properties)
    {
        selfHandle = GCHandle.Alloc(this);
        interfaceTypeName = Marshal.StringToHGlobalAnsi(SPA_TYPE_INTERFACE_Node);

        node = (SpaNode*)NativeMemory.AllocZeroed((nuint)sizeof(SpaNode));
        methods = (SpaNodeMethods*)NativeMemory.AllocZeroed((nuint)sizeof(SpaNodeMethods));
        nodeParams = (SpaParamInfo*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(SpaParamInfo)));
        portParams = (SpaParamInfo*)NativeMemory.AllocZeroed((nuint)(5 * sizeof(SpaParamInfo)));

        methods->Version = SPA_VERSION_NODE_METHODS;
        methods->AddListener = &OnAddListener;
        methods->SetCallbacks = &OnSetCallbacks;
        methods->Sync = &OnSync;
        methods->EnumParams = &OnEnumParams;
        methods->SetParam = &OnSetParam;
        methods->SetIo = &OnSetIo;
        methods->SendCommand = &OnSendCommand;
        methods->AddPort = &OnAddPort;
        methods->RemovePort = &OnRemovePort;
        methods->PortEnumParams = &OnPortEnumParams;
        methods->PortSetParam = &OnPortSetParam;
        methods->PortUseBuffers = &OnPortUseBuffers;
        methods->PortSetIo = &OnPortSetIo;
        methods->PortReuseBuffer = &OnPortReuseBuffer;
        methods->Process = &OnProcess;

        node->Interface.Type = interfaceTypeName;
        node->Interface.Version = SPA_VERSION_NODE;
        node->Interface.Callbacks.Funcs = (IntPtr)methods;
        node->Interface.Callbacks.Data = (void*)GCHandle.ToIntPtr(selfHandle);

        nodeParams[0] = CreateParamInfo(SPA_PARAM_EnumFormat, SPA_PARAM_INFO_READ);
        nodeParams[1] = CreateParamInfo(SPA_PARAM_Format, SPA_PARAM_INFO_READ | SPA_PARAM_INFO_WRITE);

        portParams[0] = CreateParamInfo(SPA_PARAM_EnumFormat, SPA_PARAM_INFO_READ);
        portParams[1] = CreateParamInfo(SPA_PARAM_Meta, SPA_PARAM_INFO_READ);
        portParams[2] = CreateParamInfo(SPA_PARAM_IO, SPA_PARAM_INFO_READ);
        portParams[3] = CreateParamInfo(SPA_PARAM_Format, SPA_PARAM_INFO_WRITE);
        portParams[4] = CreateParamInfo(SPA_PARAM_Buffers, SPA_PARAM_INFO_READ);

        exportProperties = properties;
        exportProxy = pw_core_export(core, SPA_TYPE_INTERFACE_Node, properties, (IntPtr)node, 0);
        if (exportProxy == IntPtr.Zero)
        {
            throw new VirtualCameraException("Не удалось экспортировать SPA video node в PipeWire.");
        }
    }

    internal uint TryGetBoundId() => exportProxy != IntPtr.Zero ? pw_proxy_get_bound_id(exportProxy) : PW_ID_ANY;

    internal CameraControlRange? GetControlRange(CameraControlType control) => null;

    internal void ThrowControlsNotSupported()
    {
        throw new VirtualCameraException("Контролы камеры для export-based PipeWire backend пока не поддержаны.");
    }

    internal void Start()
    {
        lock (syncRoot)
        {
            started = true;
        }
    }

    internal void Stop()
    {
        lock (syncRoot)
        {
            started = false;
            if (ioBuffers is not null)
            {
                ioBuffers->Status = SPA_STATUS_OK;
                ioBuffers->BufferId = PW_ID_ANY;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        DestroyExportProxy();

        ClearBuffers();

        if (portParams is not null)
        {
            NativeMemory.Free(portParams);
            portParams = null;
        }

        if (nodeParams is not null)
        {
            NativeMemory.Free(nodeParams);
            nodeParams = null;
        }

        if (methods is not null)
        {
            NativeMemory.Free(methods);
            methods = null;
        }

        if (node is not null)
        {
            NativeMemory.Free(node);
            node = null;
        }

        if (interfaceTypeName != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(interfaceTypeName);
            interfaceTypeName = IntPtr.Zero;
        }

        if (exportProperties != IntPtr.Zero)
        {
            pw_properties_free(exportProperties);
            exportProperties = IntPtr.Zero;
        }

        if (selfHandle.IsAllocated)
        {
            selfHandle.Free();
        }
    }

    internal void DestroyExportProxy()
    {
        if (exportProxy != IntPtr.Zero)
        {
            pw_proxy_destroy(exportProxy);
            exportProxy = IntPtr.Zero;
        }
    }

    private static SpaParamInfo CreateParamInfo(uint id, uint flags) => new()
    {
        Id = id,
        Flags = flags,
    };

    private static PipeWireExportedVideoSourceNode GetSelf(void* objectPointer)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)objectPointer);
        return (PipeWireExportedVideoSourceNode)handle.Target!;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnAddListener(void* objectPointer, SpaHook* listener, SpaNodeEvents* events, void* data)
    {
        var self = GetSelf(objectPointer);

        lock (self.syncRoot)
        {
            self.nodeEvents = events;
            self.nodeEventsData = data;
            self.EmitNodeInfo(full: true);
            self.EmitPortInfo(full: true);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSetCallbacks(void* objectPointer, SpaNodeCallbacks* callbacks, void* data)
    {
        var self = GetSelf(objectPointer);
        lock (self.syncRoot)
        {
            self.nodeCallbacks = callbacks;
            self.nodeCallbacksData = data;
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSync(void* objectPointer, int seq)
    {
        var self = GetSelf(objectPointer);
        lock (self.syncRoot)
        {
            self.EmitResult(seq, res: 0, type: 0, result: null);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEnumParams(void* objectPointer, int seq, uint id, uint start, uint num, SpaPod* filter)
    {
        var self = GetSelf(objectPointer);
        if (num == 0)
        {
            return -22;
        }

        lock (self.syncRoot)
        {
            for (uint index = start, emitted = 0; emitted < num; index++)
            {
                if (!self.TryGetParamPod(id, index, out var podBytes))
                {
                    break;
                }

                fixed (byte* podPtr = podBytes)
                {
                    var result = new SpaResultNodeParams
                    {
                        Id = id,
                        Index = index,
                        Next = index + 1,
                        Param = (SpaPod*)podPtr,
                    };

                    self.EmitResult(seq, res: 0, SPA_RESULT_TYPE_NODE_PARAMS, &result);
                }

                emitted++;
            }
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSetParam(void* objectPointer, uint id, uint flags, SpaPod* param)
    {
        var self = GetSelf(objectPointer);
        if (id == SPA_PARAM_Format)
        {
            lock (self.syncRoot)
            {
                self.currentFormatPod = param is null ? null : self.CopyPod(param);
                self.configured = param is not null;
                self.nodeParams[1] = CreateParamInfo(
                    SPA_PARAM_Format,
                    self.configured ? SPA_PARAM_INFO_READ | SPA_PARAM_INFO_WRITE : SPA_PARAM_INFO_WRITE);
                self.portParams[3] = CreateParamInfo(
                    SPA_PARAM_Format,
                    self.configured ? SPA_PARAM_INFO_READ | SPA_PARAM_INFO_WRITE : SPA_PARAM_INFO_WRITE);
                self.EmitNodeInfo(full: false);
                self.EmitPortInfo(full: false);
            }

            return 0;
        }

        return id == 0 ? -2 : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSetIo(void* objectPointer, uint id, void* data, nuint size)
    {
        return id == 0 ? -2 : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSendCommand(void* objectPointer, SpaCommand* command)
    {
        var self = GetSelf(objectPointer);
        if (command is null || command->Body.Type != SPA_TYPE_COMMAND_Node)
        {
            return -22;
        }

        lock (self.syncRoot)
        {
            switch (command->Body.Id)
            {
                case SPA_NODE_COMMAND_Start:
                    if (!self.configured || self.bufferCount == 0)
                    {
                        return -5;
                    }

                    self.started = true;
                    return 0;
                case SPA_NODE_COMMAND_Pause:
                case SPA_NODE_COMMAND_Suspend:
                    self.started = false;
                    return 0;
                default:
                    return -95;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnAddPort(void* objectPointer, uint direction, uint portId, IntPtr props)
    {
        return -95;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnRemovePort(void* objectPointer, uint direction, uint portId)
    {
        return -95;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPortEnumParams(void* objectPointer, int seq, uint direction, uint portId, uint id, uint start, uint num, SpaPod* filter)
    {
        var self = GetSelf(objectPointer);
        if (direction != SPA_DIRECTION_OUTPUT || portId != 0 || num == 0)
        {
            return -22;
        }

        lock (self.syncRoot)
        {
            for (uint index = start, emitted = 0; emitted < num; index++)
            {
                if (!self.TryGetParamPod(id, index, out var podBytes))
                {
                    break;
                }

                fixed (byte* podPtr = podBytes)
                {
                    var result = new SpaResultNodeParams
                    {
                        Id = id,
                        Index = index,
                        Next = index + 1,
                        Param = (SpaPod*)podPtr,
                    };

                    self.EmitResult(seq, res: 0, SPA_RESULT_TYPE_NODE_PARAMS, &result);
                }

                emitted++;
            }
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPortSetParam(void* objectPointer, uint direction, uint portId, uint id, uint flags, SpaPod* param)
    {
        var self = GetSelf(objectPointer);
        if (direction != SPA_DIRECTION_OUTPUT || portId != 0)
        {
            return -22;
        }

        if (id != SPA_PARAM_Format)
        {
            return -2;
        }

        lock (self.syncRoot)
        {
            self.currentFormatPod = param is null ? null : self.CopyPod(param);
            self.configured = param is not null;
            self.portParams[3] = CreateParamInfo(SPA_PARAM_Format,
                self.configured ? SPA_PARAM_INFO_READ | SPA_PARAM_INFO_WRITE : SPA_PARAM_INFO_WRITE);
            self.portParams[4] = CreateParamInfo(SPA_PARAM_Buffers, SPA_PARAM_INFO_READ);
            self.EmitPortInfo(full: false);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPortUseBuffers(void* objectPointer, uint direction, uint portId, uint flags, SpaBuffer** spaBuffers, uint nBuffers)
    {
        var self = GetSelf(objectPointer);
        if (direction != SPA_DIRECTION_OUTPUT || portId != 0)
        {
            return -22;
        }

        lock (self.syncRoot)
        {
            self.ClearBuffers();

            if (nBuffers > 0 && !self.configured)
            {
                return -5;
            }

            if (nBuffers > MaxBuffers)
            {
                return -28;
            }

            for (uint index = 0; index < nBuffers; index++)
            {
                self.RegisterBuffer(index, spaBuffers[index]);
            }

            self.bufferCount = (int)nBuffers;
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPortSetIo(void* objectPointer, uint direction, uint portId, uint id, void* data, nuint size)
    {
        var self = GetSelf(objectPointer);
        if (direction != SPA_DIRECTION_OUTPUT || portId != 0)
        {
            return -22;
        }

        if (id != SPA_IO_Buffers)
        {
            return -2;
        }

        if (size < (nuint)sizeof(SpaIoBuffers))
        {
            return -28;
        }

        lock (self.syncRoot)
        {
            self.ioBuffers = (SpaIoBuffers*)data;
            if (self.ioBuffers is not null)
            {
                self.ioBuffers->Status = SPA_STATUS_OK;
                self.ioBuffers->BufferId = PW_ID_ANY;
            }
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnPortReuseBuffer(void* objectPointer, uint portId, uint bufferId)
    {
        var self = GetSelf(objectPointer);
        if (portId != 0)
        {
            return -22;
        }

        lock (self.syncRoot)
        {
            if (bufferId < self.bufferCount)
            {
                self.buffers[bufferId].Outstanding = false;
            }
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnProcess(void* objectPointer)
    {
        var self = GetSelf(objectPointer);
        lock (self.syncRoot)
        {
            if (!self.started)
            {
                return SPA_STATUS_OK;
            }

            if (self.ioBuffers is null)
            {
                return -5;
            }

            if (self.ioBuffers->Status == SPA_STATUS_HAVE_DATA)
            {
                return SPA_STATUS_HAVE_DATA;
            }

            if (self.ioBuffers->BufferId != PW_ID_ANY && self.ioBuffers->BufferId < self.bufferCount)
            {
                self.buffers[self.ioBuffers->BufferId].Outstanding = false;
                self.ioBuffers->BufferId = PW_ID_ANY;
            }

            var buffer = self.DequeueBuffer();
            if (buffer is null)
            {
                self.ioBuffers->Status = SPA_STATUS_NEED_DATA;
                return SPA_STATUS_NEED_DATA;
            }

            self.FillBuffer(buffer);
            buffer.Outstanding = true;
            self.ioBuffers->BufferId = buffer.Id;
            self.ioBuffers->Status = SPA_STATUS_HAVE_DATA;
            return SPA_STATUS_HAVE_DATA;
        }
    }

    private bool TryGetParamPod(uint id, uint index, out byte[] podBytes)
    {
        podBytes = [];

        switch (id)
        {
            case SPA_PARAM_EnumFormat when index == 0:
                podBytes = BuildFormatPod(SPA_PARAM_EnumFormat);
                return true;
            case SPA_PARAM_Format when index == 0 && configured && currentFormatPod is not null:
                podBytes = currentFormatPod;
                return true;
            case SPA_PARAM_Meta when index == 0:
                podBytes = BuildMetaHeaderPod();
                return true;
            case SPA_PARAM_IO when index == 0:
                podBytes = BuildIoBuffersPod();
                return true;
            case SPA_PARAM_Buffers when index == 0:
                podBytes = BuildBuffersPod();
                return true;
            default:
                return false;
        }
    }

    private byte[] BuildFormatPod(uint paramId)
    {
        var buffer = new byte[256];
        var size = SpaPodBuilder.BuildVideoFormatPod(buffer, settings.Width, settings.Height, settings.FrameRate, settings.PixelFormat, paramId);
        Array.Resize(ref buffer, size);
        return buffer;
    }

    private byte[] BuildMetaHeaderPod()
    {
        var buffer = new byte[128];
        var size = SpaPodBuilder.BuildMetaHeaderParamPod(buffer);
        Array.Resize(ref buffer, size);
        return buffer;
    }

    private byte[] BuildIoBuffersPod()
    {
        var buffer = new byte[128];
        var size = SpaPodBuilder.BuildIoBuffersParamPod(buffer);
        Array.Resize(ref buffer, size);
        return buffer;
    }

    private byte[] BuildBuffersPod()
    {
        var buffer = new byte[256];
        var requestedBufferCount = Math.Clamp(DefaultAdvertisedBufferCount, 2, MaxBuffers);
        var size = SpaPodBuilder.BuildBuffersParamPod(buffer, frameSize, frameStride, requestedBufferCount);
        Array.Resize(ref buffer, size);
        return buffer;
    }

    private void RegisterBuffer(uint id, SpaBuffer* buffer)
    {
        if (buffer is null || buffer->DataCount == 0)
        {
            throw new VirtualCameraException("PipeWire передал некорректный buffer skeleton.");
        }

        var state = buffers[id];
        state.Id = id;
        state.Buffer = buffer;
        state.Header = FindHeader(buffer);

        var data = &buffer->Datas[0];
        state.Chunk = data->Chunk;
        state.MaxSize = data->MaxSize;

        if (data->Data is not null)
        {
            state.Data = data->Data;
        }
        else if (data->Type == SPA_DATA_MemFd && data->Fd >= 0 && data->MaxSize > 0)
        {
            var mapped = mmap(IntPtr.Zero, data->MaxSize, PROT_READ | PROT_WRITE, MAP_SHARED, (int)data->Fd, (nint)data->MapOffset);
            if (mapped == MAP_FAILED)
            {
                throw new VirtualCameraException("Не удалось mmap буфер PipeWire MemFd.");
            }

            state.MappedAddress = mapped;
            state.MappedSize = data->MaxSize;
            state.Data = (void*)mapped;
        }
        else
        {
            throw new VirtualCameraException("Неподдерживаемый тип PipeWire video buffer memory.");
        }
    }

    private void FillBuffer(BufferState state)
    {
        NativeMemory.Clear(state.Data, state.MaxSize);

        var frame = frameProvider();
        var copySize = (uint)Math.Min(frame.Length, (int)state.MaxSize);

        fixed (byte* framePtr = frame)
        {
            Buffer.MemoryCopy(framePtr, state.Data, state.MaxSize, copySize);
        }

        if (state.Chunk is not null)
        {
            state.Chunk->Offset = 0;
            state.Chunk->Size = copySize;
            state.Chunk->Stride = frameStride;
        }

        if (state.Header is not null)
        {
            state.Header->Flags = 0;
            state.Header->Offset = 0;
            state.Header->Pts = clock.Elapsed.Ticks * 100L;
            state.Header->DtsOffset = 0;
            state.Header->Seq = (ulong)Interlocked.Increment(ref frameSequence);
        }
    }

    private BufferState? DequeueBuffer()
    {
        for (var index = 0; index < bufferCount; index++)
        {
            if (!buffers[index].Outstanding)
            {
                return buffers[index];
            }
        }

        return null;
    }

    private void ClearBuffers()
    {
        for (var index = 0; index < buffers.Length; index++)
        {
            if (buffers[index].MappedAddress != IntPtr.Zero)
            {
                _ = munmap(buffers[index].MappedAddress, buffers[index].MappedSize);
            }

            buffers[index].MappedAddress = IntPtr.Zero;
            buffers[index].MappedSize = 0;
            buffers[index].Buffer = null;
            buffers[index].Header = null;
            buffers[index].Data = null;
            buffers[index].MaxSize = 0;
            buffers[index].Chunk = null;
            buffers[index].Outstanding = false;
        }

        bufferCount = 0;
    }

    private static SpaMetaHeader* FindHeader(SpaBuffer* buffer)
    {
        for (var index = 0u; index < buffer->MetaCount; index++)
        {
            var meta = &buffer->Metas[index];
            if (meta->Type == SPA_META_Header && meta->Data is not null && meta->Size >= (uint)sizeof(SpaMetaHeader))
            {
                return (SpaMetaHeader*)meta->Data;
            }
        }

        return null;
    }

    private byte[] CopyPod(SpaPod* pod)
    {
        var size = checked((int)(sizeof(SpaPod) + pod->Size));
        var bytes = new byte[size];
        fixed (byte* target = bytes)
        {
            Buffer.MemoryCopy(pod, target, size, size);
        }

        return bytes;
    }

    private void EmitNodeInfo(bool full)
    {
        if (nodeEvents is null || nodeEvents->Info is null)
        {
            return;
        }

        var info = new SpaNodeInfo
        {
            MaxInputPorts = 0,
            MaxOutputPorts = 1,
            ChangeMask = full
                ? SPA_NODE_CHANGE_MASK_FLAGS | SPA_NODE_CHANGE_MASK_PARAMS
                : SPA_NODE_CHANGE_MASK_PARAMS,
            Flags = 0,
            Props = IntPtr.Zero,
            Params = nodeParams,
            ParamCount = 2,
        };

        nodeEvents->Info(nodeEventsData, &info);
    }

    private void EmitPortInfo(bool full)
    {
        if (nodeEvents is null || nodeEvents->PortInfo is null)
        {
            return;
        }

        var info = new SpaPortInfo
        {
            ChangeMask = full
                ? SPA_PORT_CHANGE_MASK_FLAGS | SPA_PORT_CHANGE_MASK_PARAMS
                : SPA_PORT_CHANGE_MASK_PARAMS,
            Flags = SPA_PORT_FLAG_LIVE | SPA_PORT_FLAG_PHYSICAL | SPA_PORT_FLAG_TERMINAL,
            Rate = new SpaFraction { Num = (uint)settings.FrameRate, Denom = 1 },
            Props = IntPtr.Zero,
            Params = portParams,
            ParamCount = 5,
        };

        nodeEvents->PortInfo(nodeEventsData, SPA_DIRECTION_OUTPUT, 0, &info);
    }

    private void EmitResult(int seq, int res, uint type, void* result)
    {
        if (nodeEvents is null || nodeEvents->Result is null)
        {
            return;
        }

        nodeEvents->Result(nodeEventsData, seq, res, type, result);
    }
}