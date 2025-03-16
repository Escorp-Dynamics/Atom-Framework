using Atom.Media.Filters.Video;
using BenchmarkDotNet.Attributes;

namespace Atom.Media.Tests;

public partial class VideoStreamTests
{
    [TestCase(TestName = "Тест фильтра ZoomPan"), Benchmark]
    public void ZoomPanFilterTest()
    {
        var inputPath = Path.GetFullPath($"assets/test.jpg");
        var outputPath = Path.GetFullPath($"assets/zoompan.jpg.mp4");

        using (var stream = new VideoStream(inputPath, outputPath) { IsMuted = true })
        {
            stream.WaitForEnding(TimeSpan.FromSeconds(2));

            stream.Filters = [
                new ZoomPanFilter(1f, 1.5f, TimeSpan.FromSeconds(1))
            ];

            stream.WaitForEnding(TimeSpan.FromSeconds(5));

            stream.Filters = [
                new ZoomPanFilter(1.5f, 1f, TimeSpan.FromSeconds(1))
            ];

            stream.WaitForEnding(TimeSpan.FromSeconds(5));
        }

        Assert.That(File.Exists(outputPath), Is.True);
    }

    [TestCase(TestName = "Тест фильтра Crop"), Benchmark]
    public void CropFilterTest()
    {
        var inputPath = Path.GetFullPath($"assets/test.jpg");
        var outputPath = Path.GetFullPath($"assets/crop.jpg.mp4");

        using (var stream = new VideoStream(inputPath, outputPath) { IsMuted = true })
        {
            stream.Filters = [
                new CropFilter()
            ];

            stream.WaitForEnding(TimeSpan.FromSeconds(5));
        }

        Assert.That(File.Exists(outputPath), Is.True);
    }

    [TestCase(TestName = "Тест фильтра Crop + ZoomPan"), Benchmark]
    public void ComplexFilterTest()
    {
        var inputPath = Path.GetFullPath($"/home/exomode/1.mp4");
        var outputPath = Path.GetFullPath($"assets/complex.mp4.mp4");

        using (var stream = new VideoStream(inputPath, outputPath) { IsMuted = true, IsLooped = true, })
        {
            stream.WaitForEnding(TimeSpan.FromSeconds(5));

            stream.Filters = [
                new ZoomPanFilter(1f, 1.5f, TimeSpan.FromSeconds(1)),
                //new CropFilter(),
            ];

            stream.WaitForEnding(TimeSpan.FromSeconds(5));
        }

        Assert.That(File.Exists(outputPath), Is.True);
    }
}