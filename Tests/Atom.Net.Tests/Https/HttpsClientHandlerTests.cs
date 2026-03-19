using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Net.Https;

namespace Atom.Net.Tests.Https;

[TestFixture]
public sealed class HttpsClientHandlerTests
{
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    [Test]
    public async Task HttpClientWithHttpsClientHandlerCanSendHttp11Request()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(request.Head, Does.StartWith("GET /handler HTTP/1.1\r\n"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 7\r\n\r\nhandler"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var client = new HttpClient(new HttpsClientHandler(), disposeHandler: true);
        var responseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/handler")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(responseMessage, Is.TypeOf<HttpsResponseMessage>());
        Assert.That(body, Is.EqualTo("handler"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpClientWithHttpsClientHandlerCanSendHttps11RequestOverCustomTls()
    {
        using var certificate = CreateLoopbackCertificate();
        using var listener = CreateLocalhostListener();

        var serverTask = RunTlsServerAsync(listener, certificate, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(request.Head, Does.StartWith("GET /secure-handler HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: localhost"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\n\r\nsecure-body"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            SslProtocols = SslProtocols.Tls12,
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = HttpsClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        using var client = new HttpClient(handler, disposeHandler: false);
        var responseMessage = await client.GetAsync(new Uri($"https://localhost:{GetPort(listener)}/secure-handler")).ConfigureAwait(false);
        var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(responseMessage, Is.TypeOf<HttpsResponseMessage>());
        Assert.That(body, Is.EqualTo("secure-body"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerReusesCookieContainerAcrossRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var seenFirstCookie = false;
        var seenSecondCookie = false;

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            seenFirstCookie = first.Head.Contains("Cookie:", StringComparison.OrdinalIgnoreCase);
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nSet-Cookie: sid=abc; Path=/\r\nConnection: close\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);
        }, async stream =>
        {
            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            seenSecondCookie = second.Head.Contains("Cookie: sid=abc", StringComparison.OrdinalIgnoreCase);
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler();
        using var client = new HttpClient(handler, disposeHandler: false);

        _ = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/first")).ConfigureAwait(false);
        var secondResponseMessage = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/second")).ConfigureAwait(false);
        var secondBody = await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(seenFirstCookie, Is.False);
        Assert.That(seenSecondCookie, Is.True);
        Assert.That(secondBody, Is.EqualTo("ok"));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerReusesSingleTcpConnectionForSequentialRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /one HTTP/1.1\r\n"));
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /two HTTP/1.1\r\n"));
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler();
        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotReuseConnectionWhenIdleTimeoutElapsed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = RunServerAsync(listener, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        }, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.Zero,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(2));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDoesNotReuseConnectionWhenLifetimeElapsed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var acceptCount = 0;
        var serverTask = RunServerAsync(listener, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        }, async stream =>
        {
            Interlocked.Increment(ref acceptCount);
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            PooledConnectionLifetime = TimeSpan.Zero,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        var first = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/one")).ConfigureAwait(false);
        var second = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/two")).ConfigureAwait(false);

        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(2));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDirectSendHonorsMaxConnectionsPerServerForConcurrentRequests()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var firstRequestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /one HTTP/1.1\r\n"));
            firstRequestSeen.SetResult();

            await releaseFirstResponse.Task.ConfigureAwait(false);
            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /two HTTP/1.1\r\n"));
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        var firstTask = InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/one")), CancellationToken.None);

        await firstRequestSeen.Task.ConfigureAwait(false);

        var secondTask = InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/two")), CancellationToken.None);
        await Task.Delay(150).ConfigureAwait(false);

        Assert.That(listener.Pending(), Is.False);

        releaseFirstResponse.SetResult();

        var firstResponseMessage = await firstTask.ConfigureAwait(false);
        var secondResponseMessage = await secondTask.ConfigureAwait(false);

        Assert.That(await firstResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerReusesSingleConnectionAcrossLongSequentialSeries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        const int requestCount = 12;
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            for (var i = 0; i < requestCount; i++)
            {
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                Assert.That(request.Head, Does.StartWith($"GET /series/{i} HTTP/1.1\r\n"));

                var close = i == requestCount - 1 ? "Connection: close\r\n" : string.Empty;
                var body = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{close}\r\n{body}");
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        using var client = new HttpClient(handler, disposeHandler: false);

        for (var i = 0; i < requestCount; i++)
        {
            var response = await client.GetAsync(new Uri($"http://127.0.0.1:{GetPort(listener)}/series/{i}")).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(body, Is.EqualTo(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task HttpsClientHandlerDirectSendProcessesBurstThroughSingleLeasedConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        const int requestCount = 6;
        var acceptCount = 0;

        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            Interlocked.Increment(ref acceptCount);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

            for (var i = 0; i < requestCount; i++)
            {
                var request = await ReadRequestAsync(stream).ConfigureAwait(false);
                Assert.That(request.Head, Does.StartWith($"GET /burst/{i} HTTP/1.1\r\n"));

                var close = i == requestCount - 1 ? "Connection: close\r\n" : string.Empty;
                var body = $"b{i}";
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{close}\r\n{body}");
                await stream.WriteAsync(response).ConfigureAwait(false);
            }
        });

        using var handler = new HttpsClientHandler
        {
            MaxConnectionsPerServer = 1,
        };

        var tasks = Enumerable.Range(0, requestCount)
            .Select(index => InvokeSendInternalAsync(handler, new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/burst/{index}")), CancellationToken.None))
            .ToArray();

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

        for (var i = 0; i < responses.Length; i++)
        {
            var body = await responses[i].Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(body, Is.EqualTo($"b{i}"));
        }

        Assert.That(acceptCount, Is.EqualTo(1));
        await serverTask.ConfigureAwait(false);
    }

    private static int GetPort(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static TcpListener CreateLocalhostListener()
    {
        var listener = new TcpListener(IPAddress.IPv6Any, 0);

        try { listener.Server.DualMode = true; } catch { }

        listener.Start();
        return listener;
    }

    private static async Task RunTlsServerAsync(TcpListener listener, X509Certificate2 certificate, Func<SslStream, Task> handler)
    {
        using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
        using var networkStream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
        using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);

        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12,
            ClientCertificateRequired = false,
        }).ConfigureAwait(false);

        await handler(sslStream).ConfigureAwait(false);
    }

    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    private static async Task<HttpsResponseMessage> InvokeSendInternalAsync(HttpsClientHandler handler, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = typeof(HttpsClientHandler).GetMethod("SendInternalAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SendInternalAsync not found.");

        var task = (Task<HttpsResponseMessage>?)method.Invoke(handler, [request, cancellationToken])
            ?? throw new InvalidOperationException("SendInternalAsync invocation returned null.");

        return await task.ConfigureAwait(false);
    }

    private static async Task RunServerAsync(TcpListener listener, params Func<System.Net.Sockets.NetworkStream, Task>[] handlers)
    {
        foreach (var handler in handlers)
        {
            using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
            await handler(stream).ConfigureAwait(false);
        }
    }

    private static async Task<(string Head, byte[] Body)> ReadRequestAsync(Stream stream)
    {
        var headerBytes = await ReadUntilAsync(stream, HeaderTerminator).ConfigureAwait(false);
        var head = Encoding.ASCII.GetString(headerBytes);
        var contentLength = ParseContentLength(head);
        var body = contentLength > 0 ? await ReadExactAsync(stream, contentLength).ConfigureAwait(false) : [];
        return (head, body);
    }

    private static int ParseContentLength(string head)
    {
        foreach (var line in head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
            return int.Parse(line[(line.IndexOf(':') + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        return 0;
    }

    private static async Task<byte[]> ReadUntilAsync(Stream stream, byte[] marker)
    {
        using var buffer = new MemoryStream();
        var matched = 0;
        var one = new byte[1];

        while (matched < marker.Length)
        {
            var read = await stream.ReadAsync(one).ConfigureAwait(false);
            if (read is 0) throw new InvalidOperationException("Peer closed before header terminator.");

            buffer.WriteByte(one[0]);
            matched = one[0] == marker[matched] ? matched + 1 : one[0] == marker[0] ? 1 : 0;
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset)).ConfigureAwait(false);
            if (read is 0) throw new InvalidOperationException("Peer closed before full body.");
            offset += read;
        }

        return buffer;
    }
}