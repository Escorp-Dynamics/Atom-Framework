using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Atom.Net.Tests.Support;

namespace Atom.Net.Tests.Udp;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class UdpStreamBenchmarkSuite
{
    private const int PayloadSize = 128;
    private readonly byte[] payload = CreatePayload(PayloadSize);

    [Benchmark(Description = "UDP connected datagram 128B")]
    public async Task ConnectedDatagramBaseline()
    {
        var (leftSocket, rightSocket) = await LoopbackSocketFactory.CreateUdpPairAsync().ConfigureAwait(false);

        using var left = new Net.Udp.UdpStream(leftSocket, new Net.Udp.UdpSettings
        {
            UsePacketInfo = false,
            AttemptTimeout = TimeSpan.FromSeconds(2),
        }, ownsSocket: true);
        using var right = new Net.Udp.UdpStream(rightSocket, new Net.Udp.UdpSettings
        {
            UsePacketInfo = false,
            AttemptTimeout = TimeSpan.FromSeconds(2),
        }, ownsSocket: true);

        await left.WriteAsync(payload).ConfigureAwait(false);

        var buffer = new byte[payload.Length];
        _ = await right.ReadAsync(buffer).ConfigureAwait(false);
    }

    private static byte[] CreatePayload(int size)
    {
        var data = new byte[size];

        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 31) & 0xFF);

        return data;
    }
}