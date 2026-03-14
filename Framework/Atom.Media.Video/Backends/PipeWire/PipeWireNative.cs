#pragma warning disable CA1069, CA2101, CA5392, SYSLIB1054

using System.Runtime.InteropServices;

namespace Atom.Media.Video.Backends.PipeWire;

/// <summary>
/// P/Invoke привязки к libpipewire-0.3.
/// </summary>
internal static unsafe partial class PipeWireNative
{
    private const string Lib = "libpipewire-0.3.so.0";

    // ═══════════════════════════════════════════════════════════════
    // Константы
    // ═══════════════════════════════════════════════════════════════

    /// <summary>PW_DIRECTION_OUTPUT = 1.</summary>
    internal const int PW_DIRECTION_OUTPUT = 1;

    /// <summary>PW_ID_ANY = 0xFFFFFFFF.</summary>
    internal const uint PW_ID_ANY = 0xFFFFFFFF;

    /// <summary>PW_STREAM_FLAG_MAP_BUFFERS.</summary>
    internal const int PW_STREAM_FLAG_MAP_BUFFERS = 1 << 2;

    /// <summary>PW_STREAM_FLAG_DRIVER.</summary>
    internal const int PW_STREAM_FLAG_DRIVER = 1 << 3;

    /// <summary>PW_VERSION_STREAM_EVENTS.</summary>
    internal const uint PW_VERSION_STREAM_EVENTS = 2;

    // Состояния PipeWire stream
    internal const int PW_STREAM_STATE_ERROR = -1;
    internal const int PW_STREAM_STATE_UNCONNECTED = 0;
    internal const int PW_STREAM_STATE_CONNECTING = 1;
    internal const int PW_STREAM_STATE_PAUSED = 2;
    internal const int PW_STREAM_STATE_STREAMING = 3;

    // SPA_PROP видео контролы
    internal const uint SPA_PROP_brightness = 0x20001;
    internal const uint SPA_PROP_contrast = 0x20002;
    internal const uint SPA_PROP_saturation = 0x20003;
    internal const uint SPA_PROP_hue = 0x20004;
    internal const uint SPA_PROP_gamma = 0x20005;
    internal const uint SPA_PROP_exposure = 0x20006;
    internal const uint SPA_PROP_gain = 0x20007;
    internal const uint SPA_PROP_sharpness = 0x20008;

    // ═══════════════════════════════════════════════════════════════
    // Инициализация
    // ═══════════════════════════════════════════════════════════════

    [DllImport(Lib, EntryPoint = "pw_init")]
    internal static extern void pw_init(IntPtr argc, IntPtr argv);

    [DllImport(Lib, EntryPoint = "pw_deinit")]
    internal static extern void pw_deinit();

    // ═══════════════════════════════════════════════════════════════
    // Thread Loop
    // ═══════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════
    // Context
    // ═══════════════════════════════════════════════════════════════

    [DllImport(Lib, EntryPoint = "pw_context_new")]
    internal static extern IntPtr pw_context_new(IntPtr loop, IntPtr properties, nuint userDataSize);

    [DllImport(Lib, EntryPoint = "pw_context_destroy")]
    internal static extern void pw_context_destroy(IntPtr context);

    [DllImport(Lib, EntryPoint = "pw_context_connect")]
    internal static extern IntPtr pw_context_connect(IntPtr context, IntPtr properties, nuint userDataSize);

    // ═══════════════════════════════════════════════════════════════
    // Core
    // ═══════════════════════════════════════════════════════════════

    [DllImport(Lib, EntryPoint = "pw_core_disconnect")]
    internal static extern int pw_core_disconnect(IntPtr core);

    // ═══════════════════════════════════════════════════════════════
    // Properties
    // ═══════════════════════════════════════════════════════════════

    [DllImport(Lib, EntryPoint = "pw_properties_new")]
    internal static extern IntPtr pw_properties_new(IntPtr sentinel);

    [DllImport(Lib, EntryPoint = "pw_properties_set")]
    internal static extern int pw_properties_set(IntPtr props,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(Lib, EntryPoint = "pw_properties_free")]
    internal static extern void pw_properties_free(IntPtr props);

    // ═══════════════════════════════════════════════════════════════
    // Stream
    // ═══════════════════════════════════════════════════════════════

    [DllImport(Lib, EntryPoint = "pw_stream_new")]
    internal static extern IntPtr pw_stream_new(IntPtr core,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr properties);

    [DllImport(Lib, EntryPoint = "pw_stream_destroy")]
    internal static extern void pw_stream_destroy(IntPtr stream);

    [DllImport(Lib, EntryPoint = "pw_stream_connect")]
    internal static extern int pw_stream_connect(IntPtr stream,
        int direction, uint targetId, int flags,
        IntPtr* paramPods, uint paramCount);

    [DllImport(Lib, EntryPoint = "pw_stream_disconnect")]
    internal static extern int pw_stream_disconnect(IntPtr stream);

    [DllImport(Lib, EntryPoint = "pw_stream_set_active")]
    internal static extern int pw_stream_set_active(IntPtr stream, [MarshalAs(UnmanagedType.U1)] bool active);

    [DllImport(Lib, EntryPoint = "pw_stream_dequeue_buffer")]
    internal static extern PwBuffer* pw_stream_dequeue_buffer(IntPtr stream);

    [DllImport(Lib, EntryPoint = "pw_stream_queue_buffer")]
    internal static extern int pw_stream_queue_buffer(IntPtr stream, PwBuffer* buffer);

    [DllImport(Lib, EntryPoint = "pw_stream_set_control")]
    internal static extern int pw_stream_set_control(IntPtr stream, uint id, uint nValues, float* values);

    [DllImport(Lib, EntryPoint = "pw_stream_get_control")]
    internal static extern PwStreamControl* pw_stream_get_control(IntPtr stream, uint id);

    [DllImport(Lib, EntryPoint = "pw_stream_add_listener")]
    internal static extern void pw_stream_add_listener(IntPtr stream,
        SpaHook* listener, PwStreamEvents* events, void* data);

    [DllImport(Lib, EntryPoint = "pw_core_add_listener")]
    internal static extern void pw_core_add_listener(IntPtr core,
        SpaHook* listener, PwCoreEvents* events, void* data);

    // ═══════════════════════════════════════════════════════════════
    // Структуры
    // ═══════════════════════════════════════════════════════════════

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
    internal struct PwStreamEvents
    {
        public uint Version;
        public delegate* unmanaged[Cdecl]<void*, void> Destroy;
        public delegate* unmanaged[Cdecl]<void*, int, int, byte*, void> StateChanged;
        public delegate* unmanaged[Cdecl]<void*, uint, PwStreamControl*, void> ControlInfo;
        public IntPtr IoChanged;
        public delegate* unmanaged[Cdecl]<void*, uint, IntPtr, void> ParamChanged;
        public IntPtr AddBuffer;
        public IntPtr RemoveBuffer;
        public delegate* unmanaged[Cdecl]<void*, void> Process;
        public IntPtr Drained;
        public IntPtr Command;
        public IntPtr TriggerDone;
    }

    /// <summary>
    /// pw_stream_control — информация о контроле стрима.
    /// </summary>
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

    /// <summary>
    /// PipeWire core events — мониторинг соединения с daemon.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PwCoreEvents
    {
        public uint Version;
        public IntPtr Info;
        public IntPtr Done;
        public IntPtr Ping;
        public delegate* unmanaged[Cdecl]<void*, uint, int, int, byte*, void> Error;
        public IntPtr RemoveId;
        public IntPtr BoundId;
        public IntPtr AddMem;
        public IntPtr RemoveMem;
        public IntPtr BoundProps;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PwBuffer
    {
        public SpaBuffer* Buffer;
        public void* UserData;
        public ulong Size;
        public ulong Requested;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpaBuffer
    {
        public IntPtr Metas;
        public uint MetaCount;
        public SpaData* Datas;
        public uint DataCount;
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
    internal struct SpaChunk
    {
        public uint Offset;
        public uint Size;
        public int Stride;
        public int Flags;
    }
}
