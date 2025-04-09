namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> AVI)"), Benchmark]
    public void MkvToAviTest() => BaseTest("mkv", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> FLV)"), Benchmark]
    public void MkvToFlvTest() => BaseTest("mkv", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> MKV)"), Benchmark]
    public void MkvToMkvTest() => BaseTest("mkv", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> MOV)"), Benchmark]
    public void MkvToMovTest() => BaseTest("mkv", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> MPEG)"), Benchmark]
    public void MkvToMpegTest() => BaseTest("mkv", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> MP4)"), Benchmark]
    public void MkvToMp4Test() => BaseTest("mkv", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> WMV)"), Benchmark]
    public void MkvToWmvTest() => BaseTest("mkv", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MKV -> WEBM)"), Benchmark]
    public void MkvToWebmTest() => BaseTest("mkv", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> MKV)"), Benchmark]
    public void NoiseToMkvTest() => BaseTest(string.Empty, "mkv");
}