using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> JPG)"), Benchmark]
    public void WebpToJpgTest() => BaseTest("webp", "jpg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> PNG)"), Benchmark]
    public void WebpToPngTest() => BaseTest("webp", "png");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> WEBP)"), Benchmark]
    public void WebpToWebpTest() => BaseTest("webp", "webp");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> AVI)"), Benchmark]
    public void WebpToAviTest() => BaseTest("webp", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> FLV)"), Benchmark]
    public void WebpToFlvTest() => BaseTest("webp", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> MKV)"), Benchmark]
    public void WebpToMkvTest() => BaseTest("webp", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> MOV)"), Benchmark]
    public void WebpToMovTest() => BaseTest("webp", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> MPEG)"), Benchmark]
    public void WebpToMpegTest() => BaseTest("webp", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> MP4)"), Benchmark]
    public void WebpToMp4Test() => BaseTest("webp", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> WMV)"), Benchmark]
    public void WebpToWmvTest() => BaseTest("webp", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBP -> WEBM)"), Benchmark]
    public void WebpToWebmTest() => BaseTest("webp", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> WEBP)"), Benchmark]
    public void NoiseToWebpTest() => BaseTest(string.Empty, "webp");
}