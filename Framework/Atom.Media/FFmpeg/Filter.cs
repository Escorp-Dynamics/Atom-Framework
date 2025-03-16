using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public static unsafe partial class Filter
    {
        private const string Dll = "avfilter";

        [LibraryImport(Dll, EntryPoint = "avfilter_graph_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial FilterGraph* GraphAlloc();

        [LibraryImport(Dll, EntryPoint = "avfilter_get_by_name", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void* GetByName([MarshalAs(UnmanagedType.LPStr)] string? name);

        [LibraryImport(Dll, EntryPoint = "avfilter_graph_create_filter", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int CreateInGraph(void** filterContext, void* filter, [MarshalAs(UnmanagedType.LPStr)] string? name, [MarshalAs(UnmanagedType.LPStr)] string? args, void* opaque, void* graphContext);

        [LibraryImport(Dll, EntryPoint = "av_buffersrc_add_frame_flags", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int AddFrameFlagsToContext(void* filterContext, MediaFrame* frame, int flags);

        [LibraryImport(Dll, EntryPoint = "av_buffersink_get_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int GetFrameBufferSink(void* filterContext, MediaFrame* frame);

        [LibraryImport(Dll, EntryPoint = "avfilter_inout_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaFilterInOut* InOutAlloc();

        [LibraryImport(Dll, EntryPoint = "avfilter_graph_parse_ptr", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ParseGraphSpecs(void* graphContext, [MarshalAs(UnmanagedType.LPStr)] string? filters, MediaFilterInOut** inputs, MediaFilterInOut** outputs, void* log_ctx);

        [LibraryImport(Dll, EntryPoint = "avfilter_graph_config", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int GraphConfig(void* graphContext, void* log_ctx);

        [LibraryImport(Dll, EntryPoint = "avfilter_graph_free", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void GraphFree(void** graphContext);

        [LibraryImport(Dll, EntryPoint = "avfilter_inout_free", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void InOutFree(MediaFilterInOut** ctx);
    }
}