using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public unsafe static partial class Util
    {
        const string Dll = "avutil";

        [LibraryImport(Dll, EntryPoint = "av_frame_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaFrame* FrameAlloc();

        [LibraryImport(Dll, EntryPoint = "av_packet_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaPacket* PacketAlloc();

        [LibraryImport(Dll, EntryPoint = "av_frame_get_buffer", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FrameGetBuffer(MediaFrame* frame, int align);

        [LibraryImport(Dll, EntryPoint = "av_frame_free", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void FrameFree(MediaFrame** frame);
    }
}