using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Atom.Net.Tests.Support;

namespace Atom.Net.Tests.Udp;

[ShortRunJob]
[CancelAfter(TestTimeoutMs * 4)]
public class UdpStreamLoadTests(ILogger logger) : BenchmarkTests<UdpStreamLoadTests>(logger)
{
    private const string BenchmarkEnvVar = "ATOM_RUN_BENCHMARKS";
    private const int BurstCount = 512;
    private const int PayloadSize = 128;
    private const int TestTimeoutMs = 12000;
    private const double MinDatagramsPerSecond = 1000;

    public UdpStreamLoadTests() : this(ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
    }

    [TestCase(TestName = "UDP BenchmarkDotNet")]
    public override void RunBenchmarks()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
        TestContext.Out.WriteLine($"UDP benchmark env: {IsBenchmarkEnabled}");
        if (!IsBenchmarkEnabled) return;

        TestContext.Out.WriteLine("Starting UDP BenchmarkRunner");
        var summary = BenchmarkRunner.Run<UdpStreamBenchmarkSuite>();
        Assert.That(summary, Is.Not.Null);
    }

    [TestCase(TestName = "UDP connected baseline"), Benchmark(Description = "UDP connected datagram 128B")]
    public async Task ConnectedDatagramBaseline()
    {
        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = CreateStream(leftSocket);
        using var right = CreateStream(rightSocket);

        var payload = CreatePayload(PayloadSize);
        await Within(left.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var read = await Within(right.ReadAsync(buffer)).ConfigureAwait(false);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(read, Is.EqualTo(payload.Length));
            Assert.That(buffer, Is.EqualTo(payload));
        }
    }

    [Test]
    public async Task BurstStressTransfersAllDatagramsWithoutLossOnLoopback()
    {
        if (IsBenchmarkEnabled) Assert.Ignore("Stress thresholds выполняются только в обычном test-mode");

        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = CreateStream(leftSocket);
        using var right = CreateStream(rightSocket);

        var payload = CreatePayload(PayloadSize);
        var buffer = new byte[payload.Length];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            for (var i = 0; i < BurstCount; i++)
            {
                payload[0] = (byte)(i & 0xFF);

                await Within(left.WriteAsync(payload)).ConfigureAwait(false);

                var read = await Within(right.ReadAsync(buffer)).ConfigureAwait(false);
                Assert.That(read, Is.EqualTo(payload.Length));
                Assert.That(buffer, Is.EqualTo(payload));
            }
        }
        finally
        {
            stopwatch.Stop();
        }

        var datagramsPerSecond = BurstCount / stopwatch.Elapsed.TotalSeconds;
        Logger.WriteLine(LogKind.Default, $"UDP burst stress: {BurstCount} datagrams in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(datagramsPerSecond, Is.GreaterThanOrEqualTo(MinDatagramsPerSecond));
    }

    [Test]
    public async Task PacketInfoBurstStressKeepsPacketInfoAvailable()
    {
        if (IsBenchmarkEnabled) Assert.Ignore("Stress thresholds выполняются только в обычном test-mode");

        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = new Atom.Net.Udp.UdpStream(leftSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = true }, ownsSocket: true);
        using var right = CreateStream(rightSocket);

        var payload = CreatePayload(PayloadSize);
        var buffer = new byte[payload.Length];

        for (var i = 0; i < 64; i++)
        {
            payload[0] = (byte)(255 - i);
            await Within(right.WriteAsync(payload)).ConfigureAwait(false);

            var read = await Within(left.ReadAsync(buffer)).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(payload.Length));
            Assert.That(buffer, Is.EqualTo(payload));
            Assert.That(left.LastPacketInfo.HasValue, Is.True);
        }
    }

    private static Atom.Net.Udp.UdpStream CreateStream(Socket socket)
        => new(socket, new Atom.Net.Udp.UdpSettings
        {
            UsePacketInfo = false,
            AttemptTimeout = TimeSpan.FromSeconds(2),
        }, ownsSocket: true);

    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];

        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)((i * 31) & 0xFF);

        return payload;
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}