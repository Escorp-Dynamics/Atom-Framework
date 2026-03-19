using System.Net;
using System.Net.Sockets;
using Atom.Net.Tests.Support;

namespace Atom.Net.Tests.Tcp;

[CancelAfter(TestTimeoutMs * 2)]
public sealed class TcpStreamTests
{
    private const int ParallelClientCount = 8;
    private const int TestTimeoutMs = 8000;

    [Test]
    public async Task ConnectAsyncEstablishesLoopbackConnectionAndTransfersData()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
            {
                AttemptTimeout = TimeSpan.FromSeconds(2),
            });

            var acceptTask = listener.AcceptSocketAsync();
            await Within(stream.ConnectAsync(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port)).ConfigureAwait(false);
            using var serverSocket = await Within(acceptTask).ConfigureAwait(false);

            Assert.That(stream.IsConnected, Is.True);
            Assert.That(stream.RemoteEndPoint, Is.Not.Null);

            var request = "tcp-request"u8.ToArray();
            await Within(stream.WriteAsync(request)).ConfigureAwait(false);

            var serverBuffer = new byte[request.Length];
            var serverRead = await Within(serverSocket.ReceiveAsync(serverBuffer)).ConfigureAwait(false);
            Assert.That(serverRead, Is.EqualTo(request.Length));
            Assert.That(serverBuffer, Is.EqualTo(request));

            var response = "tcp-response"u8.ToArray();
            await Within(serverSocket.SendAsync(response)).ConfigureAwait(false);

            var clientBuffer = new byte[response.Length];
            var clientRead = await Within(stream.ReadAsync(clientBuffer)).ConfigureAwait(false);
            Assert.That(clientRead, Is.EqualTo(response.Length));
            Assert.That(clientBuffer, Is.EqualTo(response));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task ExistingConnectedSocketCanBeWrappedByTcpStream()
    {
        var (client, server) = await Within(LoopbackSocketFactory.CreateTcpPairAsync()).ConfigureAwait(false);

        using var serverSocket = server;
        using var stream = new Atom.Net.Tcp.TcpStream(client, new Atom.Net.Tcp.TcpSettings
        {
            Dscp = 10,
        }, ownsSocket: true);

        var payload = "wrapped-socket"u8.ToArray();
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var serverBuffer = new byte[payload.Length];
        var serverRead = await Within(serverSocket.ReceiveAsync(serverBuffer)).ConfigureAwait(false);

        Assert.That(serverRead, Is.EqualTo(payload.Length));
        Assert.That(serverBuffer, Is.EqualTo(payload));

        var trafficClass = client.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
        Assert.That(trafficClass, Is.EqualTo(10 << 2));
    }

    [Test]
    public async Task MultipleTcpStreamsCanConnectAndExchangeDataInParallel()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = AcceptAndEchoLoopAsync(listener, ParallelClientCount);
            var clientTasks = Enumerable.Range(0, ParallelClientCount).Select(index => RunTcpClientAsync(port, index));

            await Within(Task.WhenAll([.. clientTasks, serverTask])).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public void ConnectAsyncHonorsPreCanceledToken()
    {
        using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await stream.ConnectAsync(IPAddress.Loopback.ToString(), 6553, cts.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ConnectAsyncWithUnresolvableHostThrowsSocketException()
    {
        using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

        var exception = Assert.ThrowsAsync<SocketException>(async () => await stream.ConnectAsync("atom.invalid", 443).ConfigureAwait(false));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.SocketErrorCode, Is.EqualTo(SocketError.HostNotFound));
    }

    [Test]
    public async Task ConnectAsyncWithShortAttemptTimeoutStaysWithinBudget()
    {
        using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromMilliseconds(150),
            UseHappyEyeballsAlternating = false,
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = default;

        try
        {
            await Within(stream.ConnectAsync("203.0.113.1", 65000)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            stopwatch.Stop();
        }

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<SocketException>());
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public async Task ConnectAsyncWithLocalhostCanReachIpv6Listener()
    {
        using var listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
        listener.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        listener.Listen(backlog: 1);

        var localhostAddresses = await Dns.GetHostAddressesAsync("localhost").ConfigureAwait(false);
        if (!localhostAddresses.Any(static address => address.AddressFamily is AddressFamily.InterNetworkV6))
            Assert.Ignore("localhost не резолвится в IPv6 на этой среде");

        using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

        var acceptTask = listener.AcceptAsync();
        try
        {
            await Within(stream.ConnectAsync("localhost", ((IPEndPoint)listener.LocalEndPoint!).Port)).ConfigureAwait(false);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.AddressNotAvailable)
        {
            Assert.Ignore($"IPv6 loopback недоступен для localhost на этой среде: {ex.SocketErrorCode}");
        }

        using var serverSocket = await Within(acceptTask).ConfigureAwait(false);

        var payload = System.Text.Encoding.ASCII.GetBytes("localhost-ipv6");
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var read = await Within(serverSocket.ReceiveAsync(buffer)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(buffer, Is.EqualTo(payload));
        Assert.That(stream.RemoteEndPoint, Is.Not.Null);
        Assert.That(((IPEndPoint)stream.RemoteEndPoint!).AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
    }

    private static async Task AcceptAndEchoLoopAsync(TcpListener listener, int expectedClients)
    {
        for (var i = 0; i < expectedClients; i++)
        {
            using var socket = await Within(listener.AcceptSocketAsync()).ConfigureAwait(false);

            var buffer = new byte[32];
            var read = await Within(socket.ReceiveAsync(buffer)).ConfigureAwait(false);

            Assert.That(read, Is.GreaterThan(0));

            await Within(socket.SendAsync(buffer.AsMemory(0, read))).ConfigureAwait(false);
        }
    }

    private static async Task RunTcpClientAsync(int port, int index)
    {
        using var stream = new Atom.Net.Tcp.TcpStream(new Atom.Net.Tcp.TcpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

        await Within(stream.ConnectAsync(IPAddress.Loopback.ToString(), port)).ConfigureAwait(false);

        var payload = System.Text.Encoding.ASCII.GetBytes($"parallel-{index}");
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var response = new byte[payload.Length];
        var read = await Within(stream.ReadAsync(response)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(response, Is.EqualTo(payload));
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}