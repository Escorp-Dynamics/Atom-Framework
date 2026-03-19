using System.Net;
using System.Net.Sockets;

namespace Atom.Net.Tests.Support;

internal static class LoopbackSocketFactory
{
    public static async Task<(Socket Client, Socket Server)> CreateTcpPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var acceptTask = listener.AcceptSocketAsync();

            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port).ConfigureAwait(false);
            var server = await acceptTask.ConfigureAwait(false);

            return (client, server);
        }
        finally
        {
            listener.Stop();
        }
    }

    public static async Task<(Socket Left, Socket Right)> CreateUdpPairAsync()
    {
        var left = CreateBoundUdpSocket();
        var right = CreateBoundUdpSocket();

        await left.ConnectAsync((IPEndPoint)right.LocalEndPoint!).ConfigureAwait(false);
        await right.ConnectAsync((IPEndPoint)left.LocalEndPoint!).ConfigureAwait(false);

        return (left, right);
    }

    private static Socket CreateBoundUdpSocket()
    {
        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

        try { socket.DualMode = true; } catch { }

        socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return socket;
    }
}