using System.Runtime.Versioning;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;

namespace Atom.Media.Audio.Tests;

[TestFixture]
[SupportedOSPlatform("linux")]
public class LinuxMicrophoneBackendTests(ILogger logger) : BenchmarkTests<LinuxMicrophoneBackendTests>(logger)
{
    private static readonly bool isPipeWireAvailable = CheckPipeWireAvailable();

    public LinuxMicrophoneBackendTests() : this(ConsoleLogger.Unicode) { }

    private static bool CheckPipeWireAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            var backend = new LinuxMicrophoneBackend();
            backend.InitializeAsync(new VirtualMicrophoneSettings { Name = "PipeWire Probe Mic" }, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            backend.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (VirtualMicrophoneException)
        {
            return false;
        }
    }

    private static void RequirePipeWire()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("Тесты LinuxMicrophoneBackend запускаются только на Linux.");
        }

        if (!isPipeWireAvailable)
        {
            Assert.Ignore("PipeWire daemon недоступен.");
        }
    }

    [TestCase(TestName = "GetControlRange без инициализации → VirtualMicrophoneException")]
    public async Task GetControlRangeWithoutInitThrows()
    {
        await using var backend = new LinuxMicrophoneBackend();
        Assert.Throws<VirtualMicrophoneException>(() =>
            backend.GetControlRange(MicrophoneControlType.Volume));
    }

    [TestCase(TestName = "ControlChanged подписка/отписка безопасна")]
    public async Task ControlChangedSubscribeUnsubscribeIsSafe()
    {
        await using var backend = new LinuxMicrophoneBackend();

        EventHandler<MicrophoneControlChangedEventArgs> handler = (_, _) => { };

        Assert.DoesNotThrow(() =>
        {
            backend.ControlChanged += handler;
            backend.ControlChanged -= handler;
        });
    }

    [TestCase(TestName = "Повторный DisposeAsync микрофона безопасен")]
    public async Task DoubleDisposeIsSafe()
    {
        var backend = new LinuxMicrophoneBackend();

        await backend.DisposeAsync();

        Assert.DoesNotThrowAsync(async () => await backend.DisposeAsync());
    }

    [TestCase(TestName = "Stress: несколько init/dispose циклов микрофона безопасны")]
    public async Task InitializeDisposeCyclesAreSafe()
    {
        RequirePipeWire();

        for (var i = 0; i < 3; i++)
        {
            var backend = new LinuxMicrophoneBackend();

            await backend.InitializeAsync(
                new VirtualMicrophoneSettings { Name = "Stress Mic " + i },
                CancellationToken.None);
            await backend.DisposeAsync();

            Assert.DoesNotThrowAsync(async () => await backend.DisposeAsync());
        }
    }

    [TestCase(TestName = "Stress: start/stop/dispose микрофона в цикле безопасны")]
    public async Task StartStopDisposeCyclesAreSafe()
    {
        RequirePipeWire();

        for (var i = 0; i < 3; i++)
        {
            await using var backend = new LinuxMicrophoneBackend();

            await backend.InitializeAsync(
                new VirtualMicrophoneSettings { Name = "Capture Mic " + i },
                CancellationToken.None);
            await backend.StartCaptureAsync(CancellationToken.None);
            await backend.StopCaptureAsync(CancellationToken.None);

            Assert.That(backend.IsCapturing, Is.False);
        }
    }
}
