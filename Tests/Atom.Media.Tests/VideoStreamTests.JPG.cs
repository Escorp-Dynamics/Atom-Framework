using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> JPG)"), Benchmark]
    public void JpgToJpgTest() => BaseTest("jpg", "jpg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> PNG)"), Benchmark]
    public void JpgToPngTest() => BaseTest("jpg", "png");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> WEBP)"), Benchmark]
    public void JpgToWebpTest() => BaseTest("jpg", "webp");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> AVI)"), Benchmark]
    public void JpgToAviTest() => BaseTest("jpg", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> FLV)"), Benchmark]
    public void JpgToFlvTest() => BaseTest("jpg", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> MKV)"), Benchmark]
    public void JpgToMkvTest() => BaseTest("jpg", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> MOV)"), Benchmark]
    public void JpgToMovTest() => BaseTest("jpg", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> MPEG)"), Benchmark]
    public void JpgToMpegTest() => BaseTest("jpg", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> MP4)"), Benchmark]
    public void JpgToMp4Test() => BaseTest("jpg", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> WMV)"), Benchmark]
    public void JpgToWmvTest() => BaseTest("jpg", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (JPG -> WEBM)"), Benchmark]
    public void JpgToWebmTest() => BaseTest("jpg", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> JPG)"), Benchmark]
    public void NoiseToJpgTest() => BaseTest(string.Empty, "jpg");
}