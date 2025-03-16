using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal partial class FFmpeg
{
    public static unsafe partial class Codec
    {
        private const string Dll = "avcodec";

        [LibraryImport(Dll, EntryPoint = "avcodec_send_packet", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int SendPacket(CodecContext* context, MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "avcodec_receive_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ReceiveFrame(CodecContext* context, MediaFrame* frame);

        [LibraryImport(Dll, EntryPoint = "av_packet_unref", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int UnRefPacket(MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "avcodec_alloc_context3", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial CodecContext* AllocContext3(AVCodec* codec);

        [LibraryImport(Dll, EntryPoint = "avcodec_find_encoder", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial AVCodec* FindEncoder(MediaCodec codecId);

        [LibraryImport(Dll, EntryPoint = "avcodec_parameters_to_context", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ParametersToContext(CodecContext* context, CodecParameters* parameters);

        [LibraryImport(Dll, EntryPoint = "avcodec_open2", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int Open2(CodecContext* context, AVCodec* codec, void* options);

        [LibraryImport(Dll, EntryPoint = "avcodec_find_decoder", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial AVCodec* FindDecoder(MediaCodec codecId);

        [LibraryImport(Dll, EntryPoint = "avcodec_parameters_from_context", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ParametersFromContext(CodecParameters* parameters, CodecContext* context);

        [LibraryImport(Dll, EntryPoint = "avcodec_parameters_copy", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ParametersCopy(CodecParameters* outputParameters, CodecParameters* inputParameters);

        [LibraryImport(Dll, EntryPoint = "avcodec_send_frame", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int SendFrame(CodecContext* context, MediaFrame* frame);

        [LibraryImport(Dll, EntryPoint = "avcodec_receive_packet", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int ReceivePacket(CodecContext* context, MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "av_packet_rescale_ts", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void PacketRescaleTS(MediaPacket* packet, Ratio srcTimeBase, Ratio dstTimeBase);

        [LibraryImport(Dll, EntryPoint = "avcodec_free_context", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void FreeContext(CodecContext** context);

        [LibraryImport(Dll, EntryPoint = "av_packet_alloc", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial MediaPacket* PacketAlloc();

        [LibraryImport(Dll, EntryPoint = "av_packet_free", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void PacketFree(MediaPacket** packet);

        [LibraryImport(Dll, EntryPoint = "av_init_packet", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void InitPacket(MediaPacket* packet);

        [LibraryImport(Dll, EntryPoint = "avcodec_encode_video2", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int EncodeVideo2(CodecContext* context, MediaPacket* packet, MediaFrame* frame, out int gotPacketPtr);

        [LibraryImport(Dll, EntryPoint = "avcodec_is_open", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int IsOpen(CodecContext* context);

        [LibraryImport(Dll, EntryPoint = "av_codec_is_encoder", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int IsEncoder(AVCodec* context);

        [LibraryImport(Dll, EntryPoint = "av_codec_is_decoder", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int IsDecoder(AVCodec* context);

        [LibraryImport(Dll, EntryPoint = "avcodec_close", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int Close(CodecContext* context);

        [LibraryImport(Dll, EntryPoint = "avcodec_flush_buffers", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial int FlushBuffers(CodecContext* context);
    }
}