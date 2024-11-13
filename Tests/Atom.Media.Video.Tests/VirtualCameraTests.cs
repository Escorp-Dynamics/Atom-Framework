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

    [TestCase(TestName = "Тест захвата виртуальной камеры (локальный)"), Benchmark(Baseline = true)]
    public async Task CaptureTest()
    {
        await using var camera = new VirtualCamera() { IsMuted = true };

        await camera.StartCaptureAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.WaitForCaptureAsync(Path.GetFullPath("assets/test.mov"));
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.WaitForCaptureAsync(Path.GetFullPath("assets/test.mp4"));
        await camera.StopCaptureAsync();

        Assert.Pass();
    }

    [TestCase(TestName = "Тест захвата виртуальной камеры (удалённый)"), Benchmark]
    public async Task RemoteCaptureTest()
    {
        await using var camera = new VirtualCamera() { IsMuted = true };

        await camera.StartCaptureAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.WaitForCaptureAsync(new Uri("https://test.com/test1.mp4"));
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.WaitForCaptureAsync(new Uri("https://test.com/test2.mp4"));
        await camera.StopCaptureAsync();

        Assert.Pass();
    }

    private static void SetUp()
    {
        string[] packages = ["ffmpeg", "v4l2loopback-dkms", "v4l-utils", "v4l2loopback-utils"];

        foreach (var package in packages)
            if (!Distribution.OS.PM.CheckExistsAsync(package).AsTask().GetAwaiter().GetResult()
                && !Distribution.OS.PM.InstallAsync(package).AsTask().GetAwaiter().GetResult())
                throw new VirtualCameraException("Не удалось установить пакет");

        Distribution.OS.Terminal.RunAsAdministratorAsync($"rmmod v4l2loopback").AsTask().GetAwaiter().GetResult();
    }
}