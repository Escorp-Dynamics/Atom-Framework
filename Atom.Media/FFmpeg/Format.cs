using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public unsafe static partial class Format
    {
        const string Dll = "avformat";

        [LibraryImport(Dll, EntryPoint = "avformat_network_init", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int NetworkInit();

        [LibraryImport(Dll, EntryPoint = "av_read_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ReadFrame(FormatContext* context, MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "avformat_open_input", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int OpenInput(FormatContext** context, [MarshalAs(UnmanagedType.LPStr)] string url, void* fmt, void* options);

        [LibraryImport(Dll, EntryPoint = "avformat_find_stream_info", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FindStreamInfo(FormatContext* context, void* options);

        [LibraryImport(Dll, EntryPoint = "avformat_alloc_output_context2", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int AllocOutputContext2
        (
            FormatContext** context,
            void* outputFormat,
            [MarshalAs(UnmanagedType.LPStr)] string? formatName,
            [MarshalAs(UnmanagedType.LPStr)] string? fileName
        );

        [LibraryImport(Dll, EntryPoint = "avformat_new_stream", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaStream* NewStream(FormatContext* context, void* options);

        [LibraryImport(Dll, EntryPoint = "avio_open", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int IoOpen(IOContext** context, [MarshalAs(UnmanagedType.LPStr)] string url, int flags);

        [LibraryImport(Dll, EntryPoint = "avformat_write_header", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int WriteHeader(FormatContext* context, void* options);

        [LibraryImport(Dll, EntryPoint = "av_interleaved_write_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int InterleavedWriteFrame(FormatContext* context, MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "avformat_close_input", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void CloseInput(FormatContext** context);

        [LibraryImport(Dll, EntryPoint = "avformat_free_context", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void FreeContext(FormatContext* context);
    }
}