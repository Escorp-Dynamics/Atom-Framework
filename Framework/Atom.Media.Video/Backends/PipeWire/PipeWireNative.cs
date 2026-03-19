#pragma warning disable CA1069, CA2101, CA5392, SYSLIB1054, MA0182

using System.Runtime.InteropServices;

namespace Atom.Media.Video.Backends.PipeWire;

/// <summary>
/// P/Invoke привязки к libpipewire-0.3 и базовым ABI SPA.
/// </summary>
internal static unsafe partial class PipeWireNative
{
    private const string Lib = "libpipewire-0.3.so.0";
    private const string LibC = "libc.so.6";

    internal const string SPA_TYPE_INTERFACE_Device = "Spa:Pointer:Interface:Device";
    internal const string SPA_TYPE_INTERFACE_Node = "Spa:Pointer:Interface:Node";

    internal const int PW_DIRECTION_OUTPUT = 1;
    internal const uint PW_ID_ANY = 0xFFFFFFFF;

    internal const uint SPA_DIRECTION_OUTPUT = 1;

    internal const uint SPA_VERSION_DEVICE = 0;
    internal const uint SPA_VERSION_DEVICE_EVENTS = 0;
    internal const uint SPA_VERSION_DEVICE_METHODS = 0;
    internal const uint SPA_VERSION_DEVICE_OBJECT_INFO = 0;
    internal const uint SPA_VERSION_NODE = 0;
    internal const uint SPA_VERSION_NODE_EVENTS = 0;
    internal const uint SPA_VERSION_NODE_CALLBACKS = 0;
    internal const uint SPA_VERSION_NODE_METHODS = 0;

    internal const uint SPA_NODE_CHANGE_MASK_FLAGS = 1u << 0;
    internal const uint SPA_NODE_CHANGE_MASK_PROPS = 1u << 1;
    internal const uint SPA_NODE_CHANGE_MASK_PARAMS = 1u << 2;

    internal const uint SPA_DEVICE_CHANGE_MASK_FLAGS = 1u << 0;
    internal const uint SPA_DEVICE_CHANGE_MASK_PROPS = 1u << 1;
    internal const uint SPA_DEVICE_CHANGE_MASK_PARAMS = 1u << 2;
    internal const uint SPA_DEVICE_OBJECT_CHANGE_MASK_FLAGS = 1u << 0;
    internal const uint SPA_DEVICE_OBJECT_CHANGE_MASK_PROPS = 1u << 1;

    internal const uint SPA_PORT_CHANGE_MASK_FLAGS = 1u << 0;
    internal const uint SPA_PORT_CHANGE_MASK_PROPS = 1u << 2;
    internal const uint SPA_PORT_CHANGE_MASK_PARAMS = 1u << 3;

    internal const ulong SPA_PORT_FLAG_CAN_ALLOC_BUFFERS = 1u << 2;
    internal const ulong SPA_PORT_FLAG_NO_REF = 1u << 4;
    internal const ulong SPA_PORT_FLAG_LIVE = 1u << 5;
    internal const ulong SPA_PORT_FLAG_PHYSICAL = 1u << 6;
    internal const ulong SPA_PORT_FLAG_TERMINAL = 1u << 7;

    internal const uint SPA_PARAM_INFO_SERIAL = 1u << 0;
    internal const uint SPA_PARAM_INFO_READ = 1u << 1;
    internal const uint SPA_PARAM_INFO_WRITE = 1u << 2;

    internal const uint SPA_PARAM_EnumFormat = 3;
    internal const uint SPA_PARAM_Format = 4;
    internal const uint SPA_PARAM_Buffers = 5;
    internal const uint SPA_PARAM_Meta = 6;
    internal const uint SPA_PARAM_IO = 7;

    internal const uint SPA_PARAM_BUFFERS_buffers = 1;
    internal const uint SPA_PARAM_BUFFERS_blocks = 2;
    internal const uint SPA_PARAM_BUFFERS_size = 3;
    internal const uint SPA_PARAM_BUFFERS_stride = 4;
    internal const uint SPA_PARAM_BUFFERS_align = 5;
    internal const uint SPA_PARAM_BUFFERS_dataType = 6;
    internal const uint SPA_PARAM_BUFFERS_metaType = 7;

    internal const uint SPA_PARAM_META_type = 1;
    internal const uint SPA_PARAM_META_size = 2;

    internal const uint SPA_PARAM_IO_id = 1;
    internal const uint SPA_PARAM_IO_size = 2;

    internal const uint SPA_META_Header = 1;

    internal const uint SPA_IO_Buffers = 1;

    internal const uint SPA_DATA_MemPtr = 1;
    internal const uint SPA_DATA_MemFd = 2;

    internal const int SPA_STATUS_OK = 0;
    internal const int SPA_STATUS_NEED_DATA = 1 << 0;
    internal const int SPA_STATUS_HAVE_DATA = 1 << 1;

    internal const uint SPA_RESULT_TYPE_NODE_ERROR = 1;
    internal const uint SPA_RESULT_TYPE_NODE_PARAMS = 2;

    internal const uint SPA_TYPE_COMMAND_Node = 0x30002;
    internal const uint SPA_TYPE_OBJECT_Format = 0x40003;
    internal const uint SPA_TYPE_OBJECT_ParamBuffers = 0x40004;
    internal const uint SPA_TYPE_OBJECT_ParamMeta = 0x40005;
    internal const uint SPA_TYPE_OBJECT_ParamIO = 0x40006;

    internal const uint SPA_NODE_COMMAND_Suspend = 0;
    internal const uint SPA_NODE_COMMAND_Pause = 1;
    internal const uint SPA_NODE_COMMAND_Start = 2;

    internal const int PROT_READ = 0x1;
    internal const int PROT_WRITE = 0x2;
    internal const int MAP_SHARED = 0x01;
    internal static readonly IntPtr MAP_FAILED = new(-1);

    internal const uint SPA_PROP_brightness = 0x20001;
    internal const uint SPA_PROP_contrast = 0x20002;
    internal const uint SPA_PROP_saturation = 0x20003;
    internal const uint SPA_PROP_hue = 0x20004;
    internal const uint SPA_PROP_gamma = 0x20005;
    internal const uint SPA_PROP_exposure = 0x20006;
    internal const uint SPA_PROP_gain = 0x20007;
    internal const uint SPA_PROP_sharpness = 0x20008;

    [DllImport(Lib, EntryPoint = "pw_init")]
    internal static extern void pw_init(IntPtr argc, IntPtr argv);

    [DllImport(Lib, EntryPoint = "pw_deinit")]
    internal static extern void pw_deinit();

    [DllImport(Lib, EntryPoint = "pw_thread_loop_new")]
    internal static extern IntPtr pw_thread_loop_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? name, IntPtr props);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_destroy")]
    internal static extern void pw_thread_loop_destroy(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_start")]
    internal static extern int pw_thread_loop_start(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_stop")]
    internal static extern void pw_thread_loop_stop(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_lock")]
    internal static extern void pw_thread_loop_lock(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_unlock")]
    internal static extern void pw_thread_loop_unlock(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_thread_loop_get_loop")]
    internal static extern IntPtr pw_thread_loop_get_loop(IntPtr loop);

    [DllImport(Lib, EntryPoint = "pw_context_new")]
    internal static extern IntPtr pw_context_new(IntPtr loop, IntPtr properties, nuint userDataSize);

    [DllImport(Lib, EntryPoint = "pw_context_destroy")]
    internal static extern void pw_context_destroy(IntPtr context);

    [DllImport(Lib, EntryPoint = "pw_context_connect")]
    internal static extern IntPtr pw_context_connect(IntPtr context, IntPtr properties, nuint userDataSize);

    [DllImport(Lib, EntryPoint = "pw_core_disconnect")]
    internal static extern int pw_core_disconnect(IntPtr core);

    [DllImport(Lib, EntryPoint = "pw_core_sync")]
    internal static extern int pw_core_sync(IntPtr core, uint id, int seq);

    [DllImport(Lib, EntryPoint = "pw_core_export")]
    internal static extern IntPtr pw_core_export(
        IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string type,
        IntPtr props,
        IntPtr objectPointer,
        nuint userDataSize);

    [DllImport(Lib, EntryPoint = "pw_proxy_destroy")]
    internal static extern void pw_proxy_destroy(IntPtr proxy);

    [DllImport(Lib, EntryPoint = "pw_proxy_get_bound_id")]
    internal static extern uint pw_proxy_get_bound_id(IntPtr proxy);

    [DllImport(Lib, EntryPoint = "pw_properties_new")]
    internal static extern IntPtr pw_properties_new(IntPtr sentinel);

    [DllImport(Lib, EntryPoint = "pw_properties_set")]
    internal static extern int pw_properties_set(IntPtr props,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(Lib, EntryPoint = "pw_properties_free")]
    internal static extern void pw_properties_free(IntPtr props);

    [DllImport(Lib, EntryPoint = "pw_core_add_listener")]
    internal static extern void pw_core_add_listener(IntPtr core,
        SpaHook* listener, PwCoreEvents* events, void* data);

    [DllImport(LibC, EntryPoint = "mmap", SetLastError = true)]
    internal static extern IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport(LibC, EntryPoint = "munmap", SetLastError = true)]
    internal static extern int munmap(IntPtr addr, nuint length);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaHook
    {
        public IntPtr LinkNext;
        public IntPtr LinkPrev;
        public IntPtr CbFuncs;
        public IntPtr CbData;
        public IntPtr Removed;
        public IntPtr Priv;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaCallbacks
    {
        public IntPtr Funcs;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaInterface
    {
        public IntPtr Type;
        public uint Version;
        public SpaCallbacks Callbacks;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaNode
    {
        public SpaInterface Interface;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDevice
    {
        public SpaInterface Interface;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaFraction
    {
        public uint Num;
        public uint Denom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDict
    {
        public uint Flags;
        public uint NItems;
        public IntPtr Items;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDictItem
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PwProperties
    {
        public SpaDict Dict;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaPod
    {
        public uint Size;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaCommandBody
    {
        public uint Type;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaCommand
    {
        public SpaPod Pod;
        public SpaCommandBody Body;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaNodeEvents
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, SpaNodeInfo*, void> Info;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, SpaPortInfo*, void> PortInfo;
        public delegate* unmanaged[Cdecl]<void*, int, int, uint, void*, void> Result;
        public delegate* unmanaged[Cdecl]<void*, IntPtr, void> Event;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDeviceEvents
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, SpaDeviceInfo*, void> Info;
        public delegate* unmanaged[Cdecl]<void*, int, int, uint, void*, void> Result;
        public delegate* unmanaged[Cdecl]<void*, IntPtr, void> Event;
        public delegate* unmanaged[Cdecl]<void*, uint, SpaDeviceObjectInfo*, void> ObjectInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaNodeCallbacks
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, int, int> Ready;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, int> ReuseBuffer;
        public delegate* unmanaged[Cdecl]<void*, ulong, ulong, IntPtr, int> XRun;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaNodeMethods
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, SpaHook*, SpaNodeEvents*, void*, int> AddListener;
        public delegate* unmanaged[Cdecl]<void*, SpaNodeCallbacks*, void*, int> SetCallbacks;
        public delegate* unmanaged[Cdecl]<void*, int, int> Sync;
        public delegate* unmanaged[Cdecl]<void*, int, uint, uint, uint, SpaPod*, int> EnumParams;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, SpaPod*, int> SetParam;
        public delegate* unmanaged[Cdecl]<void*, uint, void*, nuint, int> SetIo;
        public delegate* unmanaged[Cdecl]<void*, SpaCommand*, int> SendCommand;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, IntPtr, int> AddPort;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, int> RemovePort;
        public delegate* unmanaged[Cdecl]<void*, int, uint, uint, uint, uint, uint, SpaPod*, int> PortEnumParams;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, uint, uint, SpaPod*, int> PortSetParam;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, uint, SpaBuffer**, uint, int> PortUseBuffers;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, uint, void*, nuint, int> PortSetIo;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, int> PortReuseBuffer;
        public delegate* unmanaged[Cdecl]<void*, int> Process;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDeviceMethods
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, SpaHook*, SpaDeviceEvents*, void*, int> AddListener;
        public delegate* unmanaged[Cdecl]<void*, int, int> Sync;
        public delegate* unmanaged[Cdecl]<void*, int, uint, uint, uint, SpaPod*, int> EnumParams;
        public delegate* unmanaged[Cdecl]<void*, uint, uint, SpaPod*, int> SetParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaParamInfo
    {
        public uint Id;
        public uint Flags;
        public uint User;
        public int Seq;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
        public uint Padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaNodeInfo
    {
        public uint MaxInputPorts;
        public uint MaxOutputPorts;
        public ulong ChangeMask;
        public ulong Flags;
        public IntPtr Props;
        public SpaParamInfo* Params;
        public uint ParamCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDeviceInfo
    {
        public uint Version;
        public ulong ChangeMask;
        public ulong Flags;
        public IntPtr Props;
        public SpaParamInfo* Params;
        public uint ParamCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaDeviceObjectInfo
    {
        public uint Version;
        public IntPtr Type;
        public IntPtr FactoryName;
        public ulong ChangeMask;
        public ulong Flags;
        public IntPtr Props;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaPortInfo
    {
        public ulong ChangeMask;
        public ulong Flags;
        public SpaFraction Rate;
        public IntPtr Props;
        public SpaParamInfo* Params;
        public uint ParamCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaResultNodeParams
    {
        public uint Id;
        public uint Index;
        public uint Next;
        public SpaPod* Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaMeta
    {
        public uint Type;
        public uint Size;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaChunk
    {
        public uint Offset;
        public uint Size;
        public int Stride;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaData
    {
        public uint Type;
        public uint Flags;
        public long Fd;
        public uint MapOffset;
        public uint MaxSize;
        public void* Data;
        public SpaChunk* Chunk;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaBuffer
    {
        public uint MetaCount;
        public uint DataCount;
        public SpaMeta* Metas;
        public SpaData* Datas;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaMetaHeader
    {
        public uint Flags;
        public uint Offset;
        public long Pts;
        public long DtsOffset;
        public ulong Seq;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaIoBuffers
    {
        public int Status;
        public uint BufferId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PwCoreEvents
    {
        public uint Version;
        public IntPtr Info;
        public delegate* unmanaged[Cdecl]<void*, uint, int, void> Done;
        public IntPtr Ping;
        public delegate* unmanaged[Cdecl]<void*, uint, int, int, byte*, void> Error;
        public IntPtr RemoveId;
        public IntPtr BoundId;
        public IntPtr AddMem;
        public IntPtr RemoveMem;
        public IntPtr BoundProps;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PwStreamControl
    {
        public IntPtr Name;
        public uint Flags;
        public float Default;
        public float Min;
        public float Max;
        public float* Values;
        public uint NValues;
        public uint MaxValues;
    }
}