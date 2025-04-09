namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> AVI)"), Benchmark]
    public void Mp4ToAviTest() => BaseTest("mp4", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> FLV)"), Benchmark]
    public void Mp4ToFlvTest() => BaseTest("mp4", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> MKV)"), Benchmark]
    public void Mp4ToMkvTest() => BaseTest("mp4", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> MOV)"), Benchmark]
    public void Mp4ToMovTest() => BaseTest("mp4", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> MPEG)"), Benchmark]
    public void Mp4ToMpegTest() => BaseTest("mp4", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> MP4)"), Benchmark]
    public void Mp4ToMp4Test() => BaseTest("mp4", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> WMV)"), Benchmark]
    public void Mp4ToWmvTest() => BaseTest("mp4", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MP4 -> WEBM)"), Benchmark]
    public void Mp4ToWebmTest() => BaseTest("mp4", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> MP4)"), Benchmark]
    public void NoiseToMp4Test() => BaseTest(string.Empty, "mp4");
}