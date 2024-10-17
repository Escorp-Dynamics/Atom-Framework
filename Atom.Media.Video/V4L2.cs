using System.Runtime.InteropServices;

namespace Atom.Media.Video;

internal static partial class V4L2
{
    private const string LibC = "libc";

    [LibraryImport(LibC, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Open(string path, int flags);

    [LibraryImport(LibC, EntryPoint = "close")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Close(int fd);

    [LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Ioctl(int fd, uint request, ref Capability cap);

    [LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Ioctl(int fd, uint request, ref Format fmt);

    [LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Ioctl(int fd, uint request, ref Buffer buf);

    [LibraryImport(LibC, EntryPoint = "mmap", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial nint Mmap(nint addr, uint length, int prot, int flags, int fd, uint offset);

    [LibraryImport(LibC, EntryPoint = "munmap", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Munmap(nint addr, uint length);

    [LibraryImport(LibC, EntryPoint = "write")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Write(int fd, nint buf, uint count);

    [LibraryImport(LibC, EntryPoint = "read")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Read(int fd, nint buf, uint count);

    [StructLayout(LayoutKind.Sequential)]
    public struct Capability
    {
        public uint driver;
        public uint card;
        public uint bus_info;
        public uint version;
        public uint capabilities;
        public uint reserved;
    }

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
        public uint pixelformat;
        public uint field;
        public uint bytesperline;
        public uint sizeimage;
        public uint colorspace;
        public uint priv;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Buffer
    {
        public uint index;
        public uint type;
        public uint bytesused;
        public uint flags;
        public uint field;
        public uint timestamp_sec;
        public uint timestamp_usec;
        public uint timecode_type;
        public uint timecode_flags;
        public uint timecode_frames;
        public uint timecode_seconds;
        public uint timecode_minutes;
        public uint timecode_hours;
        public uint timecode_userbits;
        public uint sequence;
        public uint memory;
        public nint m;
        public uint length;
        public uint reserved;
    }
}