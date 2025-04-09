namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> AVI)"), Benchmark]
    public void WmvToAviTest() => BaseTest("wmv", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> FLV)"), Benchmark]
    public void WmvToFlvTest() => BaseTest("wmv", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> MKV)"), Benchmark]
    public void WmvToMkvTest() => BaseTest("wmv", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> MOV)"), Benchmark]
    public void WmvToMovTest() => BaseTest("wmv", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> MPEG)"), Benchmark]
    public void WmvToMpegTest() => BaseTest("wmv", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> MP4)"), Benchmark]
    public void WmvToMp4Test() => BaseTest("wmv", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> WMV)"), Benchmark]
    public void WmvToWmvTest() => BaseTest("wmv", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (WMV -> WEBM)"), Benchmark]
    public void WmvToWebmTest() => BaseTest("wmv", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> WMV)"), Benchmark]
    public void NoiseToWmvTest() => BaseTest(string.Empty, "wmv");
}