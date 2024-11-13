using BenchmarkDotNet.Loggers;

namespace Atom.Media.Tests;

public partial class VideoStreamTests(ILogger logger) : BenchmarkTest<VideoStreamTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public VideoStreamTests() : this(ConsoleLogger.Unicode) { }

    private static void BaseTest(string inputFormat, string outputFormat)
    {
        var isNoise = string.IsNullOrEmpty(inputFormat);
        var inputPath = isNoise ? string.Empty : Path.GetFullPath($"assets/test.{inputFormat}");
        if (isNoise) inputFormat = "noise";
        var outputPath = Path.GetFullPath($"assets/result.{inputFormat}.{outputFormat}");

        if (inputFormat is "png" or "jpg" or "webp") isNoise = true;

        using (var stream = new VideoStream(inputPath, outputPath))
        {
            if (isNoise)
                stream.WaitForEnding(TimeSpan.FromSeconds(10));
            else
                stream.WaitForEnding();
        }

        Assert.That(File.Exists(outputPath), Is.True);
    }

    private static void V4L2Test(string inputFormat, string outputDevice)
    {
        var isImage = inputFormat is "png" or "jpg" or "webp";

        if (!string.IsNullOrEmpty(inputFormat)) inputFormat = Path.GetFullPath($"assets/test.{inputFormat}");
        using var stream = new VideoStream(inputFormat, outputDevice) { IsMuted = true };

        if (isImage)
            stream.WaitForEnding(TimeSpan.FromSeconds(10));
        else
            stream.WaitForEnding();

        Thread.Sleep(10000);

        stream.Input = inputFormat;

        if (isImage)
            stream.WaitForEnding(TimeSpan.FromSeconds(10));
        else
            stream.WaitForEnding();

        Assert.Pass();
    }
}