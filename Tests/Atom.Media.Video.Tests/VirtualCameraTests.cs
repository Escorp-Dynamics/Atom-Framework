using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Media.Video.Tests;

public class VirtualCameraTests(ILogger logger) : BenchmarkTest<VirtualCameraTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public VirtualCameraTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест запуска виртуальной камеры"), Benchmark(Baseline = true)]
    public async Task BaseTest()
    {
        await using var camera = new VirtualCamera();
        await camera.StartCaptureAsync();

        await Task.Delay(TimeSpan.FromMinutes(5));
        Assert.Pass();
    }
}