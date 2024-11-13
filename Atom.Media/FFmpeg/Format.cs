using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public static unsafe partial class Format
    {
        private const string Dll = "avformat";

        [LibraryImport(Dll, EntryPoint = "avformat_network_init", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void NetworkInit();

        [LibraryImport(Dll, EntryPoint = "av_read_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ReadFrame(FormatContext* context, MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "av_seek_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int SeekFrame(FormatContext* context, int streamIndex, long timestamp, int flags);

        [LibraryImport(Dll, EntryPoint = "avformat_open_input", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int OpenInput(FormatContext** context, [MarshalAs(UnmanagedType.LPStr)] string url, void* fmt, void* options);

        [LibraryImport(Dll, EntryPoint = "avformat_find_stream_info", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FindStreamInfo(FormatContext* context, void* options);

        [LibraryImport(Dll, EntryPoint = "av_find_best_stream", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FindBestStream(FormatContext* context, int mediaType, int wanted_stream_nb, int related_stream, AVCodec** codec, int flags);

        [LibraryImport(Dll, EntryPoint = "av_guess_format", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial OutputFormat* GuessFormat
        (
            [MarshalAs(UnmanagedType.LPStr)] string? shortName,
            [MarshalAs(UnmanagedType.LPStr)] string? fileName,
            [MarshalAs(UnmanagedType.LPStr)] string? mimeType
        );

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
        public static partial int IoOpen(void** context, [MarshalAs(UnmanagedType.LPStr)] string url, int flags);

        [LibraryImport(Dll, EntryPoint = "avio_closep", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int IoCloseP(void** context);

        [LibraryImport(Dll, EntryPoint = "avformat_write_header", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int WriteHeader(FormatContext* context, void* options);

        [LibraryImport(Dll, EntryPoint = "av_write_trailer", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int WriteTrailer(FormatContext* context);

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

        [LibraryImport(Dll, EntryPoint = "av_write_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int WriteFrame(FormatContext* context, MediaPacket* packet);
    }
}