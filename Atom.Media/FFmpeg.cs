using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    private const string AvFormatDll = "avformat";
    private const string AvCodecDll = "avcodec";
    private const string AvUtilDll = "avutil";

    [LibraryImport(AvFormatDll, EntryPoint = "avformat_open_input", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int OpenInput(out nint formatContext, string url, nint format, nint options);

    [LibraryImport(AvFormatDll, EntryPoint = "avformat_find_stream_info")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int FindStreamInfo(nint formatContext, nint options);

    [LibraryImport(AvFormatDll, EntryPoint = "av_find_best_stream")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int FindBestStream(nint formatContext, int type, int wantedStreamNb, int relatedStream, nint decoder, int flags);

    [LibraryImport(AvCodecDll, EntryPoint = "avcodec_open2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Open2(nint codecContext, nint codec, nint options);

    [LibraryImport(AvCodecDll, EntryPoint = "avcodec_send_packet")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int SendPacket(nint codecContext, nint packet);

    [LibraryImport(AvCodecDll, EntryPoint = "avcodec_receive_frame")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int ReceiveFrame(nint codecContext, nint frame);

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_alloc")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial nint AllocFrame();

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_free")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void FreeFrame(ref nint frame);

    [LibraryImport(AvCodecDll, EntryPoint = "av_packet_alloc")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial nint AllocPacket();

    [LibraryImport(AvCodecDll, EntryPoint = "av_packet_free")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void FreePacket(ref nint packet);

    [LibraryImport(AvCodecDll, EntryPoint = "avcodec_free_context")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void FreeContext(ref nint codecContext);

    [LibraryImport(AvFormatDll, EntryPoint = "avformat_close_input")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void CloseInput(ref nint formatContext);

    [LibraryImport(AvFormatDll, EntryPoint = "av_read_frame")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int ReadFrame(nint formatContext, nint packet);

    [LibraryImport(AvFormatDll, EntryPoint = "av_seek_frame")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int SeekFrame(nint formatContext, int streamIndex, long timestamp, int flags);

    [LibraryImport(AvUtilDll, EntryPoint = "av_image_get_buffer_size")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int GetImageBufferSize(int pixFmt, int width, int height, int align);

    [LibraryImport(AvUtilDll, EntryPoint = "av_image_fill_arrays")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int FillImageArrays(nint dstData, nint dstLineSize, [In] byte[] src, int pixFmt, int width, int height, int align);

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_set_width")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void SetFrameWidth(nint frame, int width);

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_set_height")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void SetFrameHeight(nint frame, int height);

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_set_format")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void SetFrameFormat(nint frame, int format);

    [LibraryImport(AvUtilDll, EntryPoint = "av_frame_get_buffer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int GetFrameBuffer(nint frame, int align);

    [LibraryImport("avcodec", EntryPoint = "av_packet_unref")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial void UnRefPacket(nint packet);

    [LibraryImport("avformat", EntryPoint = "avformat_seek_file")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int SeekFile(nint formatContext, int streamIndex, long minTs, long ts, long maxTs, int flags);
}