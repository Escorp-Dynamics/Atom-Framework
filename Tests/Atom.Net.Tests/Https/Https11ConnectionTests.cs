using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atom.Net.Https;
using Atom.Net.Https.Connections;

namespace Atom.Net.Tests.Https;

[TestFixture]
public sealed class Https11ConnectionTests
{
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    [Test]
    public async Task SendAsyncWritesRequestLineAndParsesFixedLengthResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("GET /probe?x=1 HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: 127.0.0.1"));
            Assert.That(request.Head, Does.Not.Contain("Connection: close"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Type: text/plain\r\nX-Test: fixed\r\n\r\nhello"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/probe?x=1"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Version, Is.EqualTo(HttpVersion.Version11));
        Assert.That(body, Is.EqualTo("hello"));
        Assert.That(response.Headers.TryGetValues("X-Test", out var values), Is.True);
        Assert.That(values, Contains.Item("fixed"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);
        Assert.That(connection.Traffic.Sended, Is.GreaterThan(0UL));
        Assert.That(connection.Traffic.Received, Is.GreaterThan(0UL));

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWritesRequestBodyAndParsesChunkedResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("POST /submit HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Content-Length: 7"));
            Assert.That(Encoding.UTF8.GetString(request.Body), Is.EqualTo("payload"));

            var response = "HTTP/1.1 201 Created\r\nTransfer-Encoding: chunked\r\nContent-Type: text/plain\r\n\r\n5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var request = new HttpsRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{GetPort(listener)}/submit"))
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
        };

        var response = await connection.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(body, Is.EqualTo("hello world"));

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncCanReuseConnectionForSequentialFramedResponses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            var first = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(first.Head, Does.StartWith("GET /first HTTP/1.1\r\n"));

            var firstResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            var second = await ReadRequestAsync(stream).ConfigureAwait(false);
            Assert.That(second.Head, Does.StartWith("GET /second HTTP/1.1\r\n"));

            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\ntwo"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var firstResponseMessage = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/first"))).ConfigureAwait(false);
        Assert.That(await firstResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("one"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);

        var secondResponseMessage = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/second"))).ConfigureAwait(false);
        Assert.That(await secondResponseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("two"));
        Assert.That(connection.IsDraining, Is.True);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWrapsMalformedStatusLineAsResponseException()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "BROKEN\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/broken"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.IsCompleted, Is.False);
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncCanUseCustomTls12Transport()
    {
        using var certificate = CreateLoopbackCertificate();
        using var listener = CreateLocalhostListener();

        var serverTask = RunTlsServerAsync(listener, certificate, async stream =>
        {
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);

            Assert.That(request.Head, Does.StartWith("GET /secure HTTP/1.1\r\n"));
            Assert.That(request.Head, Does.Contain("Host: localhost"));

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\nsecure"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        var options = CreateOptions(listener) with
        {
            Host = "localhost",
            IsHttps = true,
            SslProtocols = SslProtocols.Tls12,
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
        };

        await connection.OpenAsync(options).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"https://localhost:{GetPort(listener)}/secure"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("secure"));
        Assert.That(connection.IsSecure, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncForHeadResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Head, new Uri($"http://127.0.0.1:{GetPort(listener)}/head"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncFor204ResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/nocontent"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncFor304ResponseDoesNotExposeBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 304 Not Modified\r\nETag: abc\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/notmodified"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(body, Is.Empty);
        Assert.That(connection.IsDraining, Is.False);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncParsesChunkTrailersAndKeepsConnectionReusable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var firstResponse = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\nX-Trailer: done\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(firstResponse).ConfigureAwait(false);

            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var secondResponse = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray();
            await stream.WriteAsync(secondResponse).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var first = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/trailers"))).ConfigureAwait(false);
        Assert.That(await first.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("hello"));
        Assert.That(connection.IsDraining, Is.False);
        Assert.That(connection.HasCapacity, Is.True);

        var second = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/after-trailers"))).ConfigureAwait(false);
        Assert.That(await second.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("ok"));
        Assert.That(connection.IsDraining, Is.True);

        await connection.CloseAsync().ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncSkipsInterim100ContinueAndReturnsFinalResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 100 Continue\r\nX-Interim: yes\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nfinal"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/continue"))).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.That(response.Exception, Is.Null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("final"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWrapsResponseWhenHeadersExceedConfiguredLimit()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 200 OK\r\nX-Large: 1234567890\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        var options = CreateOptions(listener) with { MaxResponseHeadersBytes = 16 };
        await connection.OpenAsync(options).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/large-headers"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.Exception!.Message, Does.Contain("MaxResponseHeadersLength"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    [Test]
    public async Task SendAsyncWraps101UpgradeAsUnsupportedResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serverTask = RunServerAsync(listener, async stream =>
        {
            _ = await ReadRequestAsync(stream).ConfigureAwait(false);
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(response).ConfigureAwait(false);
        });

        await using var connection = new Https11Connection();
        await connection.OpenAsync(CreateOptions(listener)).ConfigureAwait(false);

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{GetPort(listener)}/upgrade"))).ConfigureAwait(false);

        Assert.That(response.Exception, Is.Not.Null);
        Assert.That(response.Exception, Is.TypeOf<NotSupportedException>());
        Assert.That(response.Exception!.Message, Does.Contain("101 Switching Protocols"));
        Assert.That(connection.IsDraining, Is.True);

        await serverTask.ConfigureAwait(false);
    }

    private static HttpsConnectionOptions CreateOptions(TcpListener listener) => new()
    {
        Host = "127.0.0.1",
        Port = GetPort(listener),
        IsHttps = false,
        PreferredVersion = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        ResponseHeadersTimeout = TimeSpan.FromSeconds(3),
        SslProtocols = SslProtocols.None,
        CheckCertificateRevocationList = true,
        ServerCertificateValidationCallback = null,
        MaxResponseHeadersBytes = 64 * 1024,
        IdleTimeout = TimeSpan.FromSeconds(30),
        MaxConcurrentStreams = 1,
        AutoDecompression = false,
    };

    private static int GetPort(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static TcpListener CreateLocalhostListener()
    {
        var listener = new TcpListener(IPAddress.IPv6Any, 0);

        try { listener.Server.DualMode = true; } catch { }

        listener.Start();
        return listener;
    }

    private static async Task RunServerAsync(TcpListener listener, Func<System.Net.Sockets.NetworkStream, Task> handler)
    {
        using var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
        using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
        await handler(stream).ConfigureAwait(false);
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

            var value = line[(line.IndexOf(':') + 1)..].Trim();
            return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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