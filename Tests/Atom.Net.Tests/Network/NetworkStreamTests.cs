using System.Net.Sockets;
using Atom.Net.Tests.Support;

namespace Atom.Net.Tests.Network;

[CancelAfter(TestTimeoutMs * 2)]
public sealed class NetworkStreamTests
{
    private const int TestTimeoutMs = 8000;

    [Test]
    public async Task WriteAsyncAndReadAsyncTransferBytesAcrossConnectedSockets()
    {
        var (client, server) = await Within(LoopbackSocketFactory.CreateTcpPairAsync()).ConfigureAwait(false);

        await using var stream = new TestNetworkStream(client, ownsSocket: true);
        using var serverSocket = server;

        var request = "ping"u8.ToArray();
        await Within(stream.WriteAsync(request)).ConfigureAwait(false);

        var serverBuffer = new byte[request.Length];
        var serverRead = await Within(serverSocket.ReceiveAsync(serverBuffer)).ConfigureAwait(false);

        Assert.That(serverRead, Is.EqualTo(request.Length));
        Assert.That(serverBuffer, Is.EqualTo(request));

        var response = "pong"u8.ToArray();
        await Within(serverSocket.SendAsync(response)).ConfigureAwait(false);

        var clientBuffer = new byte[response.Length];
        var clientRead = await Within(stream.ReadAsync(clientBuffer)).ConfigureAwait(false);

        Assert.That(clientRead, Is.EqualTo(response.Length));
        Assert.That(clientBuffer, Is.EqualTo(response));
    }

    [Test]
    public void ShutdownSendBlocksSubsequentWrites()
    {
        var pair = Within(LoopbackSocketFactory.CreateTcpPairAsync()).GetAwaiter().GetResult();

        using var serverSocket = pair.Server;
        using var stream = new TestNetworkStream(pair.Client, ownsSocket: true);

        stream.Shutdown(SocketShutdown.Send);

        Assert.That(stream.IsSendShutdown, Is.True);
        Assert.Throws<IOException>(() => stream.Write([1]));
    }

    [Test]
    public void ShutdownReceiveMakesReadReturnZero()
    {
        var pair = Within(LoopbackSocketFactory.CreateTcpPairAsync()).GetAwaiter().GetResult();

        using var serverSocket = pair.Server;
        using var stream = new TestNetworkStream(pair.Client, ownsSocket: true);

        stream.Shutdown(SocketShutdown.Receive);

        var buffer = new byte[8];
        var read = stream.Read(buffer);

        Assert.That(stream.IsReceiveShutdown, Is.True);
        Assert.That(read, Is.Zero);
    }

    private sealed class TestNetworkStream(Socket socket, bool ownsSocket) : Atom.Net.NetworkStream(socket, ownsSocket);

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}