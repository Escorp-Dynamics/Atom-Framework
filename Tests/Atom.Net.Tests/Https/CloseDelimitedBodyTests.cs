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

/// <summary>
/// Проверяет ответ, тело которого ограничено ЗАКРЫТИЕМ соединения.
/// </summary>
/// <remarks>
/// ★ Это законная и распространённая форма HTTP/1.1: ни <c>Content-Length</c>, ни кусочной
/// передачи в ответе нет, и конец тела объявляется разрывом (RFC 9112, §6.3). Так отвечают
/// корневые адреса craigslist.org и cisco.com — «302 Found» с одним лишь заголовком Location, — и
/// на них обоих наш клиент падал с сообщением «Разрыв соединения при чтении TLS».
///
/// Диагноз выглядел сетевым и потому уводил поиск в приветствие, хотя рукопожатие к тому моменту
/// давно состоялось, запрос ушёл и ответ пришёл ЦЕЛИКОМ: поток TLS 1.2 превращал обычный FIN на
/// границе записи в исключение, и весь ответ пропадал вместе с ним. Поток TLS 1.3 то же самое
/// обрабатывает верно, отчего беда казалась избирательной по узлам — проявлялась ровно там, где
/// сервер остался на TLS 1.2.
///
/// Здесь сервер закрывает сокет БЕЗ предупреждения close_notify — именно так, как это делают
/// настоящие узлы: <see cref="SslStream"/> отправляет предупреждение только по явному
/// <c>ShutdownAsync</c>, а простое закрытие даёт голый FIN.
/// </remarks>
[TestFixture]
[CancelAfter(TestTimeoutMs)]
public sealed class CloseDelimitedBodyTests
{
    private const int TestTimeoutMs = 30_000;

    private const string Response =
        "HTTP/1.1 302 Found\r\n"
        + "Location: https://example.org/\r\n"
        + "Strict-Transport-Security: max-age=63072000\r\n"
        + "\r\n"
        + "переезд";

    [Test]
    public async Task ResponseSurvivesPeerCloseWithoutCloseNotifyOverTls12()
    {
        using var certificate = CreateLoopbackCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RespondAndDropAsync(listener, certificate);

        await using var connection = new Https11Connection();

        await connection.OpenAsync(new HttpsConnectionOptions
        {
            Host = "localhost",
            Port = port,
            IsHttps = true,
            PreferredVersion = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ResponseHeadersTimeout = TimeSpan.FromSeconds(10),
            RequestSendTimeout = TimeSpan.FromSeconds(10),
            ResponseBodyTimeout = TimeSpan.FromSeconds(10),
            SslProtocols = SslProtocols.Tls12,
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            MaxResponseHeadersBytes = 64 * 1024,
            IdleTimeout = TimeSpan.FromSeconds(30),
            MaxConcurrentStreams = 1,
            AutoDecompression = false,
        });

        var response = await connection.SendAsync(new HttpsRequestMessage(HttpMethod.Get, new Uri($"https://localhost:{port}/")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Exception, Is.Null);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("https://example.org/"));
            Assert.That(body, Is.EqualTo("переезд"), "тело обязано дойти целиком, а не потеряться вместе с разрывом");
        });

        await serverTask;
    }

    /// <summary>
    /// Отдаёт ответ без длины и обрывает соединение, не предупредив close_notify.
    /// </summary>
    private static async Task RespondAndDropAsync(TcpListener listener, X509Certificate2 certificate)
    {
        using var socket = await listener.AcceptSocketAsync(TestContext.CurrentContext.CancellationToken);
        await using var networkStream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);
        var ssl = new SslStream(networkStream, leaveInnerStreamOpen: true);

        try
        {
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    ClientCertificateRequired = false,
                },
                TestContext.CurrentContext.CancellationToken);

            await ReadRequestHeadAsync(ssl);

            await ssl.WriteAsync(Encoding.UTF8.GetBytes(Response), TestContext.CurrentContext.CancellationToken);
            await ssl.FlushAsync(TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            // Ни ShutdownAsync, ни Dispose поверх живого сокета: нужен именно голый FIN, каким
            // обрывают соединение настоящие узлы. Порядок важен — сначала гасим сокет, и только
            // потом освобождаем обёртку, иначе она успеет договорить за нас.
            socket.Close();
            ssl.Dispose();
        }
    }

    private static async Task ReadRequestHeadAsync(SslStream stream)
    {
        var buffer = new byte[1];
        var matched = 0;

        while (matched < 4)
        {
            var read = await stream.ReadAsync(buffer, TestContext.CurrentContext.CancellationToken);
            if (read <= 0) return;

            matched = (char)buffer[0] switch
            {
                '\r' when matched is 0 or 2 => matched + 1,
                '\n' when matched is 1 or 3 => matched + 1,
                _ => 0,
            };
        }
    }

    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Закрытый ключ обязан пережить перенос: сертификат без него сервер принять не сможет.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }
}
