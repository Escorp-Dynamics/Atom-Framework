using Atom.Media.Filters.Video;

namespace Atom.Media.Video.Tests;

public class VirtualCameraTests(ILogger logger) : BenchmarkTests<VirtualCameraTests>(logger)
{
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

        await camera.StartCaptureAsync(Path.GetFullPath("/home/exomode/Загрузки/KACI MEHDI_20241230_220031.mjpeg"), true);

        await camera.WaitForCaptureAsync();

        await camera.WaitForCaptureAsync(Path.GetFullPath("assets/test.webm"));
        await camera.WaitForCaptureAsync(Path.GetFullPath("assets/test2.webm"));
        await camera.WaitForCaptureAsync(Path.GetFullPath("assets/test.mov"));

        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест зацикленного захвата виртуальной камеры (локальный)"), Benchmark]
    public async Task LoopedCaptureTest()
    {
        await using var camera = new VirtualCamera()
        {
            Resolution = new(480, 640),
            IsMuted = true,
        };

        await camera.StartCaptureAsync("https://test.com/files/idenfy/732edeefb03fcf9b4ecf6bbe5cce8065.jpg", true);

        await Task.Delay(TimeSpan.FromSeconds(10));
        await camera.StopCaptureAsync();

        await camera.StartCaptureAsync(Path.GetFullPath($"/home/exomode/1.mp4"), true);
        await Task.Delay(TimeSpan.FromSeconds(3));

        camera.Filters = [
            new ZoomPanFilter(1f, 1.65f, TimeSpan.FromSeconds(1)),
            //new CropFilter(),
        ];

        await Task.Delay(TimeSpan.FromSeconds(3));

        /*camera.Filters = [
            new ZoomPanFilter(1.65f, 2.45f, TimeSpan.FromSeconds(1)),
            //new CropFilter(),
        ];*/

        await Task.Delay(TimeSpan.FromSeconds(5));
        await camera.StopCaptureAsync();

        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест захвата виртуальной камеры (удалённый)"), Benchmark]
    public async Task RemoteCaptureTest()
    {
        await using var camera = await VirtualCamera.CreateAsync();

        await camera.StartCaptureAsync(Path.GetFullPath("assets/dummy.jpg"), true);
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.StartCaptureAsync(new Uri("https://test.com/files/idenfy/b0bcdac1daf0c0ddfe09cac8304d4c89.jpg"), true, new TransposeFilter());
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.StartCaptureAsync(new Uri("https://test.com/files/idenfy/4dc14363b64f04b4142cc158f521fdb0.mp4"), true, new TransposeFilter());
        await Task.Delay(TimeSpan.FromSeconds(10));

        await camera.ResetAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));

        await camera.StopCaptureAsync();

        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест захвата c нескольких виртуальных камер"), Benchmark]
    public async Task MultiCaptureTest()
    {
        await using var camera1 = new VirtualCamera() { IsMuted = true };
        await camera1.StartCaptureAsync("https://test.com/files/idenfy/732edeefb03fcf9b4ecf6bbe5cce8065.jpg", true);

        await using var camera2 = new VirtualCamera() { IsMuted = true };
        await camera2.StartCaptureAsync(Path.GetFullPath($"/home/exomode/1.mp4"), true);

        await Task.Delay(TimeSpan.FromMinutes(1));
        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    private static void SetUp()
    {
        Distribution.OS.Terminal.RootPassword = Environment.GetEnvironmentVariable("ROOT_PASSWORD");
        string[] packages = ["ffmpeg", "v4l2loopback-dkms", "v4l-utils", "v4l2loopback-utils"];

        foreach (var package in packages)
        {
            if (!Distribution.OS.PM.CheckExistsAsync(package).AsTask().GetAwaiter().GetResult() && !Distribution.OS.PM.InstallAsync(package).AsTask().GetAwaiter().GetResult())
                throw new VirtualCameraException("Не удалось установить пакет");
        }

        Distribution.OS.Terminal.RunAsAdministratorAndWaitAsync("rmmod v4l2loopback").AsTask().GetAwaiter().GetResult();
        Distribution.OS.Terminal.RunAsAdministratorAndWaitAsync("modprobe v4l2loopback").AsTask().GetAwaiter().GetResult();
    }
}