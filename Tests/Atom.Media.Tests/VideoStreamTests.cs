using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Media.Tests;

public class VideoStreamTests(ILogger logger) : BenchmarkTest<VideoStreamTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public VideoStreamTests() : this(ConsoleLogger.Unicode) { }

    #region AVI

    [TestCase(TestName = "Тест видеопотока с локальным файлом (AVI -> AVI)"), Benchmark(Baseline = true)]
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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> AVI)"), Benchmark]
    public void NoiseToAviTest() => BaseTest(string.Empty, "avi");

    #endregion

    #region FLV

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> FLV)"), Benchmark]
    public void NoiseToFlvTest() => BaseTest(string.Empty, "flv");

    #endregion

    #region MKV

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> MKV)"), Benchmark]
    public void NoiseToMkvTest() => BaseTest(string.Empty, "mkv");

    #endregion

    #region MOV

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> MOV)"), Benchmark]
    public void NoiseToMovTest() => BaseTest(string.Empty, "mov");

    #endregion

    #region MPEG

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> MPEG)"), Benchmark]
    public void NoiseToMpegTest() => BaseTest(string.Empty, "mpeg");

    #endregion

    #region MP4

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> MP4)"), Benchmark]
    public void NoiseToMp4Test() => BaseTest(string.Empty, "mp4");

    #endregion

    #region WMV

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

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE -> WMV)"), Benchmark]
    public void NoiseToWmvTest() => BaseTest(string.Empty, "wmv");

    #endregion

    #region V4L2

    [TestCase(TestName = "Тест видеопотока с камерой (AVI)"), Benchmark]
    public void AviToV4l2Test() => V4L2Test("avi", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (FLV)"), Benchmark]
    public void FlvToV4L2Test() => V4L2Test("flv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MKV)"), Benchmark]
    public void MkvToV4L2Test() => V4L2Test("mkv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MOV)"), Benchmark]
    public void MovToV4L2Test() => V4L2Test("mov", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MPEG)"), Benchmark]
    public void MpegToV4L2Test() => V4L2Test("mpeg", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (MP4)"), Benchmark]
    public void Mp4ToV4L2Test() => V4L2Test("mp4", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (WMV)"), Benchmark]
    public void WmvToV4L2Test() => V4L2Test("wmv", "/dev/video0");

    [TestCase(TestName = "Тест видеопотока с камерой (NOISE)"), Benchmark]
    public void NoiseToV4L2Test() => V4L2Test(string.Empty, "/dev/video0");

    #endregion

    private static void BaseTest(string inputFormat, string outputFormat)
    {
        var isNoise = string.IsNullOrEmpty(inputFormat);
        var inputPath = isNoise ? string.Empty : Path.GetFullPath($"assets/test.{inputFormat}");
        if (isNoise) inputFormat = "noise";
        var outputPath = Path.GetFullPath($"assets/result.{inputFormat}.{outputFormat}");

        using (var stream = new VideoStream(inputPath, outputPath))
        {
            if (isNoise)
                stream.WaitForEnding(TimeSpan.FromSeconds(5));
            else
                stream.WaitForEnding();
        }

        Assert.That(File.Exists(outputPath), Is.True);
    }

    private static void V4L2Test(string inputFormat, string outputDevice)
    {
        if (!string.IsNullOrEmpty(inputFormat)) inputFormat = Path.GetFullPath($"assets/test.{inputFormat}");
        using var stream = new VideoStream(inputFormat, outputDevice) { IsMuted = true, IsLooped = true, };
        stream.WaitForEnding();
        
        Assert.Pass();
    }
}