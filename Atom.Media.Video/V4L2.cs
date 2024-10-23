using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Atom.Media.Video;

/// <summary>
/// Представляет инструменты для работы с V4L2.
/// </summary>
public unsafe static partial class V4L2
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Format
    {
        public uint type;
        public PixelFormat fmt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public uint field;
        public uint bytesPerLine;
        public uint sizeImage;
        public uint colorSpace;
        public uint priv;
        public uint flags;
        public uint ycbcrEnc;
        public uint quantization;
        public uint xferFunc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Capability
    {
        public fixed byte driver[16];
        public fixed byte card[32];
        public fixed byte bus_info[32];
        public uint version;
        public uint capabilities;
        public fixed uint reserved[4];
    }

    private const string Dll = "libc";

    private const uint VIDIOC_S_FMT = 0x80CC5605;
    private const uint BUF_TYPE_VIDEO_CAPTURE = 1;
    private const uint PIX_FMT_YUYV = 0x56595559;
    private const uint FIELD_NONE = 1;
    private const uint COLORSPACE_SRGB = 1;

    private const uint VIDIOC_QUERYCAP = 0x80685600;

    [LibraryImport(Dll, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Open(string device, int flags);

    [LibraryImport(Dll, EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int close(int fd);

    [LibraryImport(Dll, EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ioctl(int fd, uint request, void* arg);

    /// <summary>
    /// Открывает устройство камеры.
    /// </summary>
    /// <param name="device">Название устройства (например, /dev/video0).</param>
    /// <returns>Дескриптор устройства.</returns>
    public static int Open(string device)
    {
        var id = Open(device, 2);
        return id < 0 ? throw new VirtualCameraException($"Не удалось открыть устройство '{device}'") : id;
    }

    /// <summary>
    /// Устанавливает формат камеры.
    /// </summary>
    /// <param name="cameraId">Идентификатор камеры.</param>
    /// <param name="resolution">Разрешение камеры.</param>
    /// <param name="format">Формат изображения.</param>
    public static void SetFormat(int cameraId, Size resolution, MediaFormat format)
    {
        var cameraFormat = new Format
        {
            type = BUF_TYPE_VIDEO_CAPTURE,
            fmt = new PixelFormat
            {
                width = (uint)resolution.Width,
                height = (uint)resolution.Height,
                pixelFormat = PIX_FMT_YUYV,
                field = FIELD_NONE,
                bytesPerLine = (uint)resolution.Width,
                sizeImage = (uint)resolution.Width * (uint)resolution.Height,
                colorSpace = COLORSPACE_SRGB,
                priv = 0,
                flags = 0,
                ycbcrEnc = 0,
                quantization = 0,
                xferFunc = 0,
            },
        };

        if (Ioctl(cameraId, VIDIOC_S_FMT, &cameraFormat) < 0)
            throw new VirtualCameraException($"Не удалось установить формат видео");
    }

    /// <summary>
    /// Устанавливает информацию о камере.
    /// </summary>
    /// <param name="cameraId">Идентификатор камеры.</param>
    /// <param name="name">Название камеры.</param>
    /// <param name="version">Версия.</param>
    /// <param name="vendor">Производитель.</param>
    public static void SetInfo(int cameraId, string name, [NotNull] Version version, string vendor)
    {
        var capability = new Capability
        {
            version = (uint)((int.Max(version.Major, 0) << 16) | (int.Max(version.Minor, 0) << 8) | int.Max(version.Revision, 0)),
            capabilities = 0x80000000 | 0x04000000 // V4L2_CAP_DEVICE_CAPS | V4L2_CAP_STREAMING
        };

        Encoding.ASCII.GetBytes(vendor + '\0').CopyTo(new Span<byte>(capability.driver, 16));
        Encoding.ASCII.GetBytes(name + '\0').CopyTo(new Span<byte>(capability.card, 32));
        Encoding.ASCII.GetBytes("platform:escorp" + '\0').CopyTo(new Span<byte>(capability.bus_info, 32));

        if (Ioctl(cameraId, VIDIOC_QUERYCAP, &capability) < 0)
            throw new VirtualCameraException($"Не удалось установить информацию о камере");

        Debug.Assert(Encoding.ASCII.GetString(capability.driver, 16).TrimEnd('\0') == vendor);
        Debug.Assert(Encoding.ASCII.GetString(capability.card, 32).TrimEnd('\0') == name);
        Debug.Assert(Encoding.ASCII.GetString(capability.bus_info, 32).TrimEnd('\0') == "platform:escorp");
    }

    /// <summary>
    /// Закрывает камеру.
    /// </summary>
    /// <param name="cameraId">Идентификатор камеры.</param>
    public static void Close(int cameraId)
    {
        if (close(cameraId) < 0) throw new VirtualCameraException($"Не удалось закрыть камеру");
    }
}