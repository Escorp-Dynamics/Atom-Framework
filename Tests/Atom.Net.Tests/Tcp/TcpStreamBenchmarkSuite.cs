using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Atom.Net.Tests.Tcp;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class TcpStreamBenchmarkSuite
{
    private const int PayloadSize = 256;
    private readonly byte[] payload = CreatePayload(PayloadSize);

    [Benchmark(Description = "TCP loopback roundtrip 256B")]
    public async Task LoopbackRoundTripBaseline()
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
            await stream.ConnectAsync(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port).ConfigureAwait(false);
            using var server = await acceptTask.ConfigureAwait(false);

            var replyTask = RunServerEchoAsync(server, payload.Length);
            await stream.WriteAsync(payload).ConfigureAwait(false);

            var buffer = new byte[payload.Length];
            _ = await stream.ReadAsync(buffer).ConfigureAwait(false);
            await replyTask.ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static byte[] CreatePayload(int size)
    {
        var data = new byte[size];

        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        return data;
    }

    private static async Task RunServerEchoAsync(Socket socket, int payloadSize)
    {
        var buffer = new byte[payloadSize];
        var read = await socket.ReceiveAsync(buffer).ConfigureAwait(false);
        await socket.SendAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
    }
}