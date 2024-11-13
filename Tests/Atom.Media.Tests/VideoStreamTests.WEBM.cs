using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> AVI)"), Benchmark]
    public void WebmToAviTest() => BaseTest("webm", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> FLV)"), Benchmark]
    public void WebmToFlvTest() => BaseTest("webm", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> MKV)"), Benchmark]
    public void WebmToMkvTest() => BaseTest("webm", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> MOV)"), Benchmark]
    public void WebmToMovTest() => BaseTest("webm", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> MPEG)"), Benchmark]
    public void WebmToMpegTest() => BaseTest("webm", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> MP4)"), Benchmark]
    public void WebmToMp4Test() => BaseTest("webm", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> WMV)"), Benchmark]
    public void WebmToWmvTest() => BaseTest("webm", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WEBM -> WEBM)"), Benchmark]
    public void WebmToWebmTest() => BaseTest("webm", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> WEBM)"), Benchmark]
    public void NoiseToWebmTest() => BaseTest(string.Empty, "webm");
}