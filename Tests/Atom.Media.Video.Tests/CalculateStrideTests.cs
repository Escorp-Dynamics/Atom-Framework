using System.Runtime.Versioning;
using Atom.Media.Video;
using Atom.Media.Video.Backends;

namespace Atom.Media.Video.Tests;

[TestFixture]
[SupportedOSPlatform("linux")]
public class CalculateStrideTests(ILogger logger) : BenchmarkTests<CalculateStrideTests>(logger)
{
    public CalculateStrideTests() : this(ConsoleLogger.Unicode) { }

    // --- RGB / BGR (3 байта на пиксель) ---

    [TestCase(VideoPixelFormat.Rgb24, TestName = "Rgb24: stride = width * 3")]
    [TestCase(VideoPixelFormat.Bgr24, TestName = "Bgr24: stride = width * 3")]
    public void ThreeBytesPerPixel(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920 * 3));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(3));
    }

    // --- RGBA / BGRA / ARGB / ABGR (4 байта на пиксель) ---

    [TestCase(VideoPixelFormat.Rgba32, TestName = "Rgba32: stride = width * 4")]
    [TestCase(VideoPixelFormat.Bgra32, TestName = "Bgra32: stride = width * 4")]
    [TestCase(VideoPixelFormat.Argb32, TestName = "Argb32: stride = width * 4")]
    [TestCase(VideoPixelFormat.Abgr32, TestName = "Abgr32: stride = width * 4")]
    public void FourBytesPerPixel(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920 * 4));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(4));
    }

    // --- YUV planar 8-bit (stride = width, только Y-plane) ---

    [TestCase(VideoPixelFormat.Yuv420P, TestName = "Yuv420P: stride = width")]
    [TestCase(VideoPixelFormat.Yuv422P, TestName = "Yuv422P: stride = width")]
    [TestCase(VideoPixelFormat.Yuv444P, TestName = "Yuv444P: stride = width")]
    public void YuvPlanar8Bit(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(1));
    }

    // --- YUV planar 10-bit (stride = width * 2) ---

    [TestCase(VideoPixelFormat.Yuv420P10Le, TestName = "Yuv420P10Le: stride = width * 2")]
    [TestCase(VideoPixelFormat.Yuv422P10Le, TestName = "Yuv422P10Le: stride = width * 2")]
    [TestCase(VideoPixelFormat.Yuv444P10Le, TestName = "Yuv444P10Le: stride = width * 2")]
    public void YuvPlanar10Bit(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920 * 2));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(2));
    }

    // --- NV12 / NV21 (stride = width) ---

    [TestCase(VideoPixelFormat.Nv12, TestName = "Nv12: stride = width")]
    [TestCase(VideoPixelFormat.Nv21, TestName = "Nv21: stride = width")]
    public void SemiPlanar(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(1));
    }

    // --- P010Le (stride = width * 2) ---

    [TestCase(TestName = "P010Le: stride = width * 2")]
    public void P010Le()
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, VideoPixelFormat.P010Le), Is.EqualTo(1920 * 2));
    }

    // --- Packed YUV (stride = width * 2) ---

    [TestCase(VideoPixelFormat.Yuyv422, TestName = "Yuyv422: stride = width * 2")]
    [TestCase(VideoPixelFormat.Uyvy422, TestName = "Uyvy422: stride = width * 2")]
    public void PackedYuv(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.EqualTo(1920 * 2));
        Assert.That(LinuxCameraBackend.CalculateStride(1, format), Is.EqualTo(2));
    }

    // --- Grayscale ---

    [TestCase(TestName = "Gray8: stride = width")]
    public void Gray8()
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, VideoPixelFormat.Gray8), Is.EqualTo(1920));
    }

    [TestCase(TestName = "Gray16Le: stride = width * 2")]
    public void Gray16Le()
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, VideoPixelFormat.Gray16Le), Is.EqualTo(1920 * 2));
    }

    // --- Unknown формат → fallback (stride = width) ---

    [TestCase(TestName = "Unknown формат: fallback stride = width")]
    public void UnknownFormatFallback()
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, VideoPixelFormat.Unknown), Is.EqualTo(1920));
    }

    // --- Стандартные разрешения ---

    [TestCase(640, 480, TestName = "SD 640x480: корректный stride для Rgba32")]
    [TestCase(1920, 1080, TestName = "Full HD 1920x1080: корректный stride для Rgba32")]
    [TestCase(3840, 2160, TestName = "4K 3840x2160: корректный stride для Rgba32")]
    [TestCase(7680, 4320, TestName = "8K 7680x4320: корректный stride для Rgba32")]
    public void StandardResolutions(int width, int height)
    {
        _ = height;
        Assert.That(LinuxCameraBackend.CalculateStride(width, VideoPixelFormat.Rgba32), Is.EqualTo(width * 4));
    }

    // --- Compressed форматы: stride = 0 ---

    [TestCase(VideoPixelFormat.Mjpeg, TestName = "Mjpeg: stride = 0")]
    [TestCase(VideoPixelFormat.H264, TestName = "H264: stride = 0")]
    [TestCase(VideoPixelFormat.Vp8, TestName = "Vp8: stride = 0")]
    [TestCase(VideoPixelFormat.Vp9, TestName = "Vp9: stride = 0")]
    public void CompressedFormatStrideIsZero(VideoPixelFormat format)
    {
        Assert.That(LinuxCameraBackend.CalculateStride(1920, format), Is.Zero);
    }
}
