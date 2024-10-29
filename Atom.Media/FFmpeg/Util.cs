using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public unsafe static partial class Util
    {
        const string Dll = "avutil";

        [LibraryImport(Dll, EntryPoint = "av_strerror", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int av_strerror(int errnum, byte* errbuf, ulong errbuf_size);

        [LibraryImport(Dll, EntryPoint = "av_frame_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaFrame* FrameAlloc();

        [LibraryImport(Dll, EntryPoint = "av_frame_make_writable", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FrameMakeWritable(MediaFrame* frame);

        [LibraryImport(Dll, EntryPoint = "av_frame_get_buffer", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FrameGetBuffer(MediaFrame* frame, int align);

        [LibraryImport(Dll, EntryPoint = "av_frame_free", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void FrameFree(MediaFrame** frame);

        [LibraryImport(Dll, EntryPoint = "av_log_set_level", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int SetLogLevel(int level);

        [LibraryImport(Dll, EntryPoint = "av_log_set_flags", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void SetLogFlags(int flags);

        [LibraryImport(Dll, EntryPoint = "av_rescale_q", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial long ReScaleQ(long a, Ratio bq, Ratio cq);

        [LibraryImport(Dll, EntryPoint = "av_rescale_q_rnd", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial long ReScaleQRnd(long a, Ratio bq, Ratio cq, int rnd);

        [LibraryImport(Dll, EntryPoint = "av_channel_layout_default", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void DefaultChannelLayout(ChannelLayout* cl, int channels);

        [LibraryImport(Dll, EntryPoint = "av_mallocz", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial byte* MemoryAllocZ(int size);

        public static string GetErrorDescription(int errorCode)
        {
            const int size = 1024;
            var buffer = stackalloc byte[size];
            var result = av_strerror(errorCode, buffer, size);

            return result < 0
                ? "Не удалось получить описание ошибки"
                : Encoding.UTF8.GetString(buffer, size).TrimEnd('\0');
        }
    }

    public static int ThrowIfErrors(this int errorCode, string message, SemaphoreSlim? locker)
    {
        if (errorCode >= 0) return errorCode;

        var descr = Util.GetErrorDescription(errorCode);
        locker?.Release();

        throw new VideoStreamException($"{message}: {descr} ({errorCode})");
    }

    public static int ThrowIfErrors(this int errorCode, string message) => errorCode.ThrowIfErrors(message, default);
}