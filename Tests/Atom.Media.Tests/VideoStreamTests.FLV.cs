using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> AVI)"), Benchmark]
    public void FlvToAviTest() => BaseTest("flv", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> FLV)"), Benchmark]
    public void FlvToFlvTest() => BaseTest("flv", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> MKV)"), Benchmark]
    public void FlvToMkvTest() => BaseTest("flv", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> MOV)"), Benchmark]
    public void FlvToMovTest() => BaseTest("flv", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> MPEG)"), Benchmark]
    public void FlvToMpegTest() => BaseTest("flv", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> MP4)"), Benchmark]
    public void FlvToMp4Test() => BaseTest("flv", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> WMV)"), Benchmark]
    public void FlvToWmvTest() => BaseTest("flv", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (FLV -> WEBM)"), Benchmark]
    public void FlvToWebmTest() => BaseTest("flv", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> FLV)"), Benchmark]
    public void NoiseToFlvTest() => BaseTest(string.Empty, "flv");
}