using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> JPG)"), Benchmark]
    public void PngToJpgTest() => BaseTest("png", "jpg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> PNG)"), Benchmark]
    public void PngToPngTest() => BaseTest("png", "png");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> WEBP)"), Benchmark]
    public void PngToWebpTest() => BaseTest("png", "webp");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> AVI)"), Benchmark]
    public void PngToAviTest() => BaseTest("png", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> FLV)"), Benchmark]
    public void PngToFlvTest() => BaseTest("png", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> MKV)"), Benchmark]
    public void PngToMkvTest() => BaseTest("png", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> MOV)"), Benchmark]
    public void PngToMovTest() => BaseTest("png", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> MPEG)"), Benchmark]
    public void PngToMpegTest() => BaseTest("png", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> MP4)"), Benchmark]
    public void PngToMp4Test() => BaseTest("png", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> WMV)"), Benchmark]
    public void PngToWmvTest() => BaseTest("png", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (PNG -> WEBM)"), Benchmark]
    public void PngToWebmTest() => BaseTest("png", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> PNG)"), Benchmark]
    public void NoiseToPngTest() => BaseTest(string.Empty, "png");
}