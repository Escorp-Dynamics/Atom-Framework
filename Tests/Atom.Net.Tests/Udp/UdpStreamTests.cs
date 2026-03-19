using System.Net;
using System.Net.Sockets;
using Atom.Net.Tests.Support;

namespace Atom.Net.Tests.Udp;

[CancelAfter(TestTimeoutMs * 2)]
public sealed class UdpStreamTests
{
    private const int DatagramBurstCount = 32;
    private const int TestTimeoutMs = 8000;

    [Test]
    public async Task ConnectedUdpStreamsCanExchangeDatagrams()
    {
        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = new Atom.Net.Udp.UdpStream(leftSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);
        using var right = new Atom.Net.Udp.UdpStream(rightSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);

        var request = "udp-left"u8.ToArray();
        await Within(left.WriteAsync(request)).ConfigureAwait(false);

        var rightBuffer = new byte[request.Length];
        var rightRead = await Within(right.ReadAsync(rightBuffer)).ConfigureAwait(false);
        Assert.That(rightRead, Is.EqualTo(request.Length));
        Assert.That(rightBuffer, Is.EqualTo(request));

        var response = "udp-right"u8.ToArray();
        await Within(right.WriteAsync(response)).ConfigureAwait(false);

        var leftBuffer = new byte[response.Length];
        var leftRead = await Within(left.ReadAsync(leftBuffer)).ConfigureAwait(false);
        Assert.That(leftRead, Is.EqualTo(response.Length));
        Assert.That(leftBuffer, Is.EqualTo(response));
    }

    [Test]
    public async Task ConnectedIpv4UdpStreamAppliesTrafficClass()
    {
        using var leftSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var rightSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        leftSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        rightSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        await Within(leftSocket.ConnectAsync((IPEndPoint)rightSocket.LocalEndPoint!)).ConfigureAwait(false);
        await Within(rightSocket.ConnectAsync((IPEndPoint)leftSocket.LocalEndPoint!)).ConfigureAwait(false);

        using var left = new Atom.Net.Udp.UdpStream(leftSocket, new Atom.Net.Udp.UdpSettings
        {
            Dscp = 11,
            UseEcn = true,
            UsePacketInfo = false,
        }, ownsSocket: true);
        using var right = new Atom.Net.Udp.UdpStream(rightSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);

        var payload = "udp-traffic-class"u8.ToArray();
        await Within(left.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var read = await Within(right.ReadAsync(buffer)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(buffer, Is.EqualTo(payload));

        var trafficClass = leftSocket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
        Assert.That(trafficClass, Is.EqualTo((11 << 2) | 0b10));
    }

    [Test]
    public async Task ConnectedUdpStreamsCanSustainBurstTraffic()
    {
        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = new Atom.Net.Udp.UdpStream(leftSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);
        using var right = new Atom.Net.Udp.UdpStream(rightSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);

        for (var i = 0; i < DatagramBurstCount; i++)
        {
            var payload = new byte[] { (byte)i, (byte)(i + 1), (byte)(255 - i) };
            await Within(left.WriteAsync(payload)).ConfigureAwait(false);

            var buffer = new byte[payload.Length];
            var read = await Within(right.ReadAsync(buffer)).ConfigureAwait(false);

            Assert.That(read, Is.EqualTo(payload.Length));
            Assert.That(buffer, Is.EqualTo(payload));
        }
    }

    [Test]
    public async Task ReadAsyncWithPacketInfoPopulatesLastPacketInfo()
    {
        var (leftSocket, rightSocket) = await Within(LoopbackSocketFactory.CreateUdpPairAsync()).ConfigureAwait(false);

        using var left = new Atom.Net.Udp.UdpStream(leftSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = true }, ownsSocket: true);
        using var right = new Atom.Net.Udp.UdpStream(rightSocket, new Atom.Net.Udp.UdpSettings { UsePacketInfo = false }, ownsSocket: true);

        var payload = "pktinfo"u8.ToArray();
        await Within(right.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var read = await Within(left.ReadAsync(buffer)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(buffer, Is.EqualTo(payload));
        Assert.That(left.LastPacketInfo.HasValue, Is.True);
        Assert.That(left.LastPacketInfo.LocalAddress, Is.Not.Null);
    }

    [Test]
    public void ConnectAsyncHonorsPreCanceledToken()
    {
        using var stream = new Atom.Net.Udp.UdpStream(new Atom.Net.Udp.UdpSettings
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
        using var stream = new Atom.Net.Udp.UdpStream(new Atom.Net.Udp.UdpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
        });

        var exception = Assert.ThrowsAsync<SocketException>(async () => await stream.ConnectAsync("atom.invalid", 443).ConfigureAwait(false));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.SocketErrorCode, Is.EqualTo(SocketError.HostNotFound));
    }

    [Test]
    public async Task ConnectAsyncWithLocalhostCanSendToIpv6Receiver()
    {
        using var receiver = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        receiver.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
        receiver.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        using var stream = new Atom.Net.Udp.UdpStream(new Atom.Net.Udp.UdpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
            UsePacketInfo = false,
        });

        await Within(stream.ConnectAsync("localhost", ((IPEndPoint)receiver.LocalEndPoint!).Port)).ConfigureAwait(false);

        var payload = System.Text.Encoding.ASCII.GetBytes("localhost-ipv6");
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var read = await Within(receiver.ReceiveAsync(buffer)).ConfigureAwait(false);

        Assert.That(read, Is.EqualTo(payload.Length));
        Assert.That(buffer, Is.EqualTo(payload));
        Assert.That(stream.RemoteEndPoint, Is.Not.Null);
        Assert.That(((IPEndPoint)stream.RemoteEndPoint!).AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
    }

    [Test]
    public async Task ConnectAsyncToBoundPeerSendsDatagramToReceiver()
    {
        using var receiver = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        try { receiver.DualMode = true; } catch { }
        receiver.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        using var stream = new Atom.Net.Udp.UdpStream(new Atom.Net.Udp.UdpSettings
        {
            AttemptTimeout = TimeSpan.FromSeconds(2),
            UsePacketInfo = false,
        });

        await Within(stream.ConnectAsync(IPAddress.IPv6Loopback.ToString(), ((IPEndPoint)receiver.LocalEndPoint!).Port)).ConfigureAwait(false);

        var payload = "udp-connected"u8.ToArray();
        await Within(stream.WriteAsync(payload)).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        var received = await Within(receiver.ReceiveAsync(buffer)).ConfigureAwait(false);

        Assert.That(received, Is.EqualTo(payload.Length));
        Assert.That(buffer, Is.EqualTo(payload));
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task Within(ValueTask task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));

    private static Task<T> Within<T>(ValueTask<T> task) => task.AsTask().WaitAsync(TimeSpan.FromMilliseconds(TestTimeoutMs));
}