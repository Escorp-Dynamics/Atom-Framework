#pragma warning disable CA1027, CA1028

namespace Atom.Media;

/// <summary>
/// Формат пикселей видеокадра.
/// </summary>
public enum VideoPixelFormat : byte
{
    /// <summary>Неизвестный формат.</summary>
    Unknown = 0,

    // ═══════════════════════════════════════════════════════════════
    // PACKED RGB/BGR (1 плоскость, все компоненты чередуются)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>RGB 24-bit (8:8:8), packed.</summary>
    Rgb24 = 1,

    /// <summary>RGBA 32-bit (8:8:8:8), packed.</summary>
    Rgba32 = 2,

    /// <summary>BGR 24-bit (8:8:8), packed.</summary>
    Bgr24 = 3,

    /// <summary>BGRA 32-bit (8:8:8:8), packed.</summary>
    Bgra32 = 4,

    /// <summary>ARGB 32-bit (8:8:8:8), packed.</summary>
    Argb32 = 5,

    /// <summary>ABGR 32-bit (8:8:8:8), packed.</summary>
    Abgr32 = 6,

    /// <summary>RGB 48-bit (16:16:16), packed, little-endian.</summary>
    Rgb48 = 7,

    /// <summary>RGBA 64-bit (16:16:16:16), packed, little-endian.</summary>
    Rgba64 = 8,

    // ═══════════════════════════════════════════════════════════════
    // PLANAR YUV (отдельные плоскости для Y, U, V)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>YUV 4:2:0 planar (3 плоскости: Y, U, V). Самый распространённый.</summary>
    Yuv420P = 16,

    /// <summary>YUV 4:2:2 planar (3 плоскости: Y, U, V).</summary>
    Yuv422P = 17,

    /// <summary>YUV 4:4:4 planar (3 плоскости: Y, U, V).</summary>
    Yuv444P = 18,

    /// <summary>YUV 4:2:0 planar, 10-bit (little-endian).</summary>
    Yuv420P10Le = 19,

    /// <summary>YUV 4:2:2 planar, 10-bit (little-endian).</summary>
    Yuv422P10Le = 20,

    /// <summary>YUV 4:4:4 planar, 10-bit (little-endian).</summary>
    Yuv444P10Le = 21,

    // ═══════════════════════════════════════════════════════════════
    // SEMI-PLANAR (Y отдельно, UV чередуются)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>NV12: Y plane + interleaved UV (4:2:0). Популярен в HW кодеках.</summary>
    Nv12 = 32,

    /// <summary>NV21: Y plane + interleaved VU (4:2:0). Android камеры.</summary>
    Nv21 = 33,

    /// <summary>P010: 10-bit NV12 (little-endian). HDR контент.</summary>
    P010Le = 34,

    // ═══════════════════════════════════════════════════════════════
    // PACKED YUV (все компоненты чередуются)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>YUYV 4:2:2 packed (Y0 U Y1 V). USB камеры.</summary>
    Yuyv422 = 48,

    /// <summary>UYVY 4:2:2 packed (U Y0 V Y1).</summary>
    Uyvy422 = 49,

    // ═══════════════════════════════════════════════════════════════
    // GRAYSCALE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Grayscale 8-bit.</summary>
    Gray8 = 64,

    /// <summary>Grayscale 16-bit (little-endian).</summary>
    Gray16Le = 65,

    /// <summary>Grayscale + Alpha 16-bit (8:8).</summary>
    GrayAlpha16 = 66,
}

/// <summary>
/// Расширения для <see cref="VideoPixelFormat"/>.
/// </summary>
public static class VideoPixelFormatExtensions
{
    /// <summary>
    /// Возвращает количество байт на пиксель для packed форматов.
    /// Для planar форматов возвращает 0.
    /// </summary>
    public static int GetBytesPerPixel(this VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Rgb24 => 3,
        VideoPixelFormat.Bgr24 => 3,
        VideoPixelFormat.Rgba32 => 4,
        VideoPixelFormat.Bgra32 => 4,
        VideoPixelFormat.Argb32 => 4,
        VideoPixelFormat.Abgr32 => 4,
        VideoPixelFormat.Rgb48 => 6,
        VideoPixelFormat.Rgba64 => 8,
        VideoPixelFormat.Gray8 => 1,
        VideoPixelFormat.Gray16Le => 2,
        VideoPixelFormat.GrayAlpha16 => 2,
        VideoPixelFormat.Yuyv422 => 2, // 4 байта на 2 пикселя
        VideoPixelFormat.Uyvy422 => 2,
        _ => 0, // planar форматы
    };

    /// <summary>
    /// Возвращает количество плоскостей для формата.
    /// </summary>
    public static int GetPlaneCount(this VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Yuv420P => 3,
        VideoPixelFormat.Yuv422P => 3,
        VideoPixelFormat.Yuv444P => 3,
        VideoPixelFormat.Yuv420P10Le => 3,
        VideoPixelFormat.Yuv422P10Le => 3,
        VideoPixelFormat.Yuv444P10Le => 3,
        VideoPixelFormat.Nv12 => 2,
        VideoPixelFormat.Nv21 => 2,
        VideoPixelFormat.P010Le => 2,
        _ => 1,
    };

    /// <summary>
    /// Возвращает true, если формат использует YUV цветовое пространство.
    /// </summary>
    public static bool IsYuv(this VideoPixelFormat format) => format is
        VideoPixelFormat.Yuv420P or VideoPixelFormat.Yuv422P or VideoPixelFormat.Yuv444P or
        VideoPixelFormat.Yuv420P10Le or VideoPixelFormat.Yuv422P10Le or VideoPixelFormat.Yuv444P10Le or
        VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 or VideoPixelFormat.P010Le or
        VideoPixelFormat.Yuyv422 or VideoPixelFormat.Uyvy422;

    /// <summary>
    /// Возвращает true, если формат использует RGB цветовое пространство.
    /// </summary>
    public static bool IsRgb(this VideoPixelFormat format) => format is
        VideoPixelFormat.Rgb24 or VideoPixelFormat.Rgba32 or
        VideoPixelFormat.Bgr24 or VideoPixelFormat.Bgra32 or
        VideoPixelFormat.Argb32 or VideoPixelFormat.Abgr32 or
        VideoPixelFormat.Rgb48 or VideoPixelFormat.Rgba64;

    /// <summary>
    /// Вычисляет размер буфера для кадра в байтах.
    /// </summary>
    public static int CalculateFrameSize(this VideoPixelFormat format, int width, int height)
    {
        // Для planar YUV форматов
        return format switch
        {
            // Packed RGB
            VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => width * height * 3,
            VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32 or
            VideoPixelFormat.Argb32 or VideoPixelFormat.Abgr32 => width * height * 4,
            VideoPixelFormat.Rgb48 => width * height * 6,
            VideoPixelFormat.Rgba64 => width * height * 8,

            // Planar YUV 4:2:0 (Y: full, U: 1/4, V: 1/4)
            VideoPixelFormat.Yuv420P => width * height * 3 / 2,
            VideoPixelFormat.Yuv420P10Le => width * height * 3, // 10-bit в 16-bit контейнере

            // Planar YUV 4:2:2 (Y: full, U: 1/2, V: 1/2)
            VideoPixelFormat.Yuv422P => width * height * 2,
            VideoPixelFormat.Yuv422P10Le => width * height * 4,

            // Planar YUV 4:4:4 (Y: full, U: full, V: full)
            VideoPixelFormat.Yuv444P => width * height * 3,
            VideoPixelFormat.Yuv444P10Le => width * height * 6,

            // Semi-planar (Y + interleaved UV)
            VideoPixelFormat.Nv12 or VideoPixelFormat.Nv21 => width * height * 3 / 2,
            VideoPixelFormat.P010Le => width * height * 3, // 10-bit

            // Packed YUV 4:2:2
            VideoPixelFormat.Yuyv422 or VideoPixelFormat.Uyvy422 => width * height * 2,

            // Grayscale
            VideoPixelFormat.Gray8 => width * height,
            VideoPixelFormat.Gray16Le or VideoPixelFormat.GrayAlpha16 => width * height * 2,

            _ => 0,
        };
    }
}
