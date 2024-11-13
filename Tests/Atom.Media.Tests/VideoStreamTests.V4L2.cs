using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с камерой (JPG)"), Benchmark]
    public void JpgToV4l2Test() => V4L2Test("jpg", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (PNG)"), Benchmark]
    public void PngToV4l2Test() => V4L2Test("png", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (WEBP)"), Benchmark]
    public void WebpToV4l2Test() => V4L2Test("webp", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (AVI)"), Benchmark]
    public void AviToV4l2Test() => V4L2Test("avi", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (FLV)"), Benchmark]
    public void FlvToV4L2Test() => V4L2Test("flv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MKV)"), Benchmark]
    public void MkvToV4L2Test() => V4L2Test("mkv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MOV)"), Benchmark]
    public void MovToV4L2Test() => V4L2Test("mov", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MPEG)"), Benchmark]
    public void MpegToV4L2Test() => V4L2Test("mpeg", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MP4)"), Benchmark]
    public void Mp4ToV4L2Test() => V4L2Test("mp4", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (WMV)"), Benchmark]
    public void WmvToV4L2Test() => V4L2Test("wmv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (WEBM)"), Benchmark]
    public void WebmToV4L2Test() => V4L2Test("webm", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE)"), Benchmark]
    public void NoiseToV4L2Test() => V4L2Test(string.Empty, "/dev/video0");
}