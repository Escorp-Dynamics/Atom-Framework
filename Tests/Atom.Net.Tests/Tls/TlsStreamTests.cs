using System.Net.Sockets;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

[CancelAfter(TestTimeoutMs * 2)]
public sealed class TlsStreamTests
{
    private const int TestTimeoutMs = 4000;

    [Test]
    public async Task HandshakeAsyncWithoutTokenHonorsConfiguredHandshakeTimeout()
    {
        using var transport = new StubNetworkStream();
        using var stream = new NeverCompletingTlsStream(transport, new TlsSettings
        {
            HandshakeTimeout = TimeSpan.FromMilliseconds(100),
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Assert.That(async () => await Within(stream.HandshakeAsync()).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
        }
        finally
        {
            stopwatch.Stop();
        }

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private sealed class StubNetworkStream() : Atom.Net.NetworkStream(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class NeverCompletingTlsStream(Atom.Net.NetworkStream stream, in TlsSettings settings) : TlsStream(stream, settings)
    {
        protected override ValueTask<bool> OnHandshakeRecordAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => ValueTask.FromResult(false);

        protected override int BuildClientHello(Span<byte> destination) => destination.Length;

        public override async ValueTask HandshakeAsync(CancellationToken cancellationToken)
            => await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}