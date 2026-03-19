using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Atom.Net.Tests.Tcp;

[ShortRunJob]
[CancelAfter(TestTimeoutMs * 4)]
public class TcpStreamLoadTests(ILogger logger) : BenchmarkTests<TcpStreamLoadTests>(logger)
{
    private const string BenchmarkEnvVar = "ATOM_RUN_BENCHMARKS";
    private const int IterationCount = 128;
    private const int ParallelClientCount = 12;
    private const int PayloadSize = 256;
    private const int TestTimeoutMs = 12000;
    private const double MinSequentialOpsPerSecond = 500;
    private const double MinParallelClientsPerSecond = 50;

    public TcpStreamLoadTests() : this(ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
    }

    [TestCase(TestName = "TCP BenchmarkDotNet")]
    public override void RunBenchmarks()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
        TestContext.Out.WriteLine($"TCP benchmark env: {IsBenchmarkEnabled}");
        if (!IsBenchmarkEnabled) return;

        TestContext.Out.WriteLine("Starting TCP BenchmarkRunner");
        var summary = BenchmarkRunner.Run<TcpStreamBenchmarkSuite>();
        Assert.That(summary, Is.Not.Null);
    }

    [TestCase(TestName = "TCP loopback baseline"), Benchmark(Description = "TCP loopback roundtrip 256B")]
    public async Task LoopbackRoundTripBaseline()
    {
        var payload = CreatePayload(PayloadSize);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            using var stream = CreateStream();
            var acceptTask = listener.AcceptSocketAsync();

            await Within(stream.ConnectAsync(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port)).ConfigureAwait(false);
            using var server = await Within(acceptTask).ConfigureAwait(false);

            var replyTask = RunServerEchoLoopAsync(server, payload.Length, iterations: 1);

            await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

            var buffer = new byte[payload.Length];
            var read = await Within(stream.ReadAsync(buffer)).ConfigureAwait(false);

            if (!IsBenchmarkEnabled)
            {
                Assert.That(read, Is.EqualTo(payload.Length));
                Assert.That(buffer, Is.EqualTo(payload));
            }

            await Within(replyTask).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task SequentialLoopbackStressCompletesWithoutFailures()
    {
        if (IsBenchmarkEnabled) Assert.Ignore("Stress thresholds выполняются только в обычном test-mode");

        var payload = CreatePayload(PayloadSize);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = AcceptSequentialClientsAsync(listener, payload, IterationCount);

            for (var i = 0; i < IterationCount; i++)
            {
                using var stream = CreateStream();
                await Within(stream.ConnectAsync(IPAddress.Loopback.ToString(), port)).ConfigureAwait(false);
                await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

                var response = new byte[payload.Length];
                var read = await Within(stream.ReadAsync(response)).ConfigureAwait(false);

                Assert.That(read, Is.EqualTo(payload.Length));
                Assert.That(response, Is.EqualTo(payload));
            }

            await Within(serverTask).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            listener.Stop();
        }

        var opsPerSecond = IterationCount / stopwatch.Elapsed.TotalSeconds;
        Logger.WriteLine(LogKind.Default, $"TCP sequential stress: {IterationCount} ops in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(opsPerSecond, Is.GreaterThanOrEqualTo(MinSequentialOpsPerSecond));
    }

    [Test]
    public async Task ParallelLoopbackStressCompletesWithinBudget()
    {
        if (IsBenchmarkEnabled) Assert.Ignore("Stress thresholds выполняются только в обычном test-mode");

        var payload = CreatePayload(PayloadSize);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = AcceptParallelClientsAsync(listener, payload, ParallelClientCount);
            var clients = Enumerable.Range(0, ParallelClientCount).Select(_ => RunParallelClientAsync(port, payload));

            await Within(Task.WhenAll([.. clients, serverTask])).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            listener.Stop();
        }

        var clientsPerSecond = ParallelClientCount / stopwatch.Elapsed.TotalSeconds;
        Logger.WriteLine(LogKind.Default, $"TCP parallel stress: {ParallelClientCount} clients in {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(clientsPerSecond, Is.GreaterThanOrEqualTo(MinParallelClientsPerSecond));
    }

    private static Atom.Net.Tcp.TcpStream CreateStream()
        => new(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];

        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i & 0xFF);

        return payload;
    }

    private static async Task AcceptSequentialClientsAsync(TcpListener listener, byte[] payload, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);
            await Within(RunServerEchoLoopAsync(socket, payload.Length, iterations: 1)).ConfigureAwait(false);
        }
    }

    private static async Task AcceptParallelClientsAsync(TcpListener listener, byte[] payload, int count)
    {
        var sockets = new Socket[count];

        try
        {
            for (var i = 0; i < count; i++)
                sockets[i] = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);

            var tasks = sockets.Select(socket => RunServerEchoLoopAsync(socket, payload.Length, iterations: 1));
            await Within(Task.WhenAll(tasks)).ConfigureAwait(false);
        }
        finally
        {
            foreach (var socket in sockets)
                socket?.Dispose();
        }
    }

    private static async Task RunParallelClientAsync(int port, byte[] payload)
    {
        using var stream = CreateStream();
        await Within(stream.ConnectAsync(IPAddress.Loopback.ToString(), port)).ConfigureAwait(false);
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var response = new byte[payload.Length];
        var read = await Within(stream.ReadAsync(response)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(response, Is.EqualTo(payload));
    }

    private static async Task RunServerEchoLoopAsync(Socket socket, int payloadSize, int iterations)
    {
        var buffer = new byte[payloadSize];

        for (var i = 0; i < iterations; i++)
        {
            var read = await Within(socket.ReceiveAsync(buffer)).ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(payloadSize));
            await Within(socket.SendAsync(buffer.AsMemory(0, read))).ConfigureAwait(false);
        }
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}