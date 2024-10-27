using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Media.Video.Tests;

public class VirtualCameraTests(ILogger logger) : BenchmarkTest<VirtualCameraTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public VirtualCameraTests() : this(ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        SetUp();
        base.OneTimeSetUp();
    }

    public override void GlobalSetUp()
    {
        SetUp();
        base.GlobalSetUp();
    }

    [TestCase(TestName = "Тест запуска виртуальной камеры"), Benchmark(Baseline = true)]
    public async Task BaseTest()
    {
        await using var camera = new VirtualCamera();

        var ex = Assert.Throws<VirtualCameraException>(camera.StartCapture);
        Assert.That(ex, Is.Null);
        
        await Task.Delay(TimeSpan.FromMinutes(5));
        Assert.Pass();
    }

    private static void SetUp()
    {
        VirtualCamera.InitAsync().AsTask().GetAwaiter().GetResult();
    }
}