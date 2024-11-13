using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> AVI)"), Benchmark]
    public void MovToAviTest() => BaseTest("mov", "avi");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> MKV)"), Benchmark]
    public void MovToFlvTest() => BaseTest("mov", "flv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> MKV)"), Benchmark]
    public void MovToMkvTest() => BaseTest("mov", "mkv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> MOV)"), Benchmark]
    public void MovToMovTest() => BaseTest("mov", "mov");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> MPEG)"), Benchmark]
    public void MovToMpegTest() => BaseTest("mov", "mpeg");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> MP4)"), Benchmark]
    public void MovToMp4Test() => BaseTest("mov", "mp4");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> WMV)"), Benchmark]
    public void MovToWmvTest() => BaseTest("mov", "wmv");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (MOV -> WEBM)"), Benchmark]
    public void MovToWebmTest() => BaseTest("mov", "webm");

    [TestCase(TestName = "Тест видеопотока с локальным файлом (NOISE -> MOV)"), Benchmark]
    public void NoiseToMovTest() => BaseTest(string.Empty, "mov");
}