namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> AVI)"), Benchmark]
    public void AviToAviTest() => BaseTest("avi", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> FLV)"), Benchmark]
    public void AviToFlvTest() => BaseTest("avi", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> MKV)"), Benchmark]
    public void AviToMkvTest() => BaseTest("avi", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> MOV)"), Benchmark]
    public void AviToMovTest() => BaseTest("avi", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> MPEG)"), Benchmark]
    public void AviToMpegTest() => BaseTest("avi", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> MP4)"), Benchmark]
    public void AviToMp4Test() => BaseTest("avi", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> WMV)"), Benchmark]
    public void AviToWmvTest() => BaseTest("avi", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> WEBM)"), Benchmark]
    public void AviToWebmTest() => BaseTest("avi", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> AVI)"), Benchmark]
    public void NoiseToAviTest() => BaseTest(string.Empty, "avi");
}