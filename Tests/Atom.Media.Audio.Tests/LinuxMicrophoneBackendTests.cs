using System.Runtime.Versioning;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;

namespace Atom.Media.Audio.Tests;

[TestFixture]
[SupportedOSPlatform("linux")]
public class LinuxMicrophoneBackendTests(ILogger logger) : BenchmarkTests<LinuxMicrophoneBackendTests>(logger)
{
    public LinuxMicrophoneBackendTests() : this(ConsoleLogger.Unicode) { }

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
}
