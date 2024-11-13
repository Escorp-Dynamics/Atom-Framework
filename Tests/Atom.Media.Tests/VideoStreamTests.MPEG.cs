using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> AVI)"), Benchmark]
    public void MpegToAviTest() => BaseTest("mpeg", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> FLV)"), Benchmark]
    public void MpegToFlvTest() => BaseTest("mpeg", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> MKV)"), Benchmark]
    public void MpegToMkvTest() => BaseTest("mpeg", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> MOV)"), Benchmark]
    public void MpegToMovTest() => BaseTest("mpeg", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> MPEG)"), Benchmark]
    public void MpegToMpegTest() => BaseTest("mpeg", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> MP4)"), Benchmark]
    public void MpegToMp4Test() => BaseTest("mpeg", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> WMV)"), Benchmark]
    public void MpegToWmvTest() => BaseTest("mpeg", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MPEG -> WEBM)"), Benchmark]
    public void MpegToWebmTest() => BaseTest("mpeg", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> MPEG)"), Benchmark]
    public void NoiseToMpegTest() => BaseTest(string.Empty, "mpeg");
}