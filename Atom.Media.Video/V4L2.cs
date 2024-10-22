using System.Runtime.InteropServices;

namespace Atom.Media.Video;

internal static partial class V4L2
{
    private const string Lib = "libv4l2.so.0";

    public const uint VIDIOC_S_FMT = 0x40000000 + 51;
    public const uint VIDIOC_S_CTRL = 0x40000000 + 28;
    public const uint BUF_TYPE_VIDEO_CAPTURE = 1;
    public const uint PIX_FMT_YUV420 = 0x32315659;
    public const uint FIELD_NONE = 1;

    public const int CID_BASE = 0x00980900;
    public const int CID_NAME = CID_BASE + 1;
    public const int CID_VERSION = CID_BASE + 2;
    public const int CID_MANUFACTURER = CID_BASE + 3;

    [LibraryImport(Lib, EntryPoint = "v4l2_open", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Open(string device, int flags);

    [LibraryImport(Lib, EntryPoint = "v4l2_close", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Close(int fd);

    [LibraryImport(Lib, EntryPoint = "v4l2_ioctl", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Ioctl(int fd, uint request, ref Format fmt);

    [LibraryImport(Lib, EntryPoint = "v4l2_ioctl", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Ioctl(int fd, uint request, ref Control ctrl);

    [StructLayout(LayoutKind.Sequential)]
    public struct Format
    {
        public uint type;
        public PixelFormat fmt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PixelFormat
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public uint field;
        public uint bytesperline;
        public uint sizeimage;
        public uint colorspace;
        public uint priv;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Control
    {
        public uint id;
        public int value;
    }
}