using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет возобновление сессии TLS 1.3 против настоящего сервера.
/// </summary>
/// <remarks>
/// ★ Возобновление — то, чего от клиента ждут ВСЕ серверы: браузер на повторный визит приходит с
/// PSK-предложением, и его отсутствие само по себе не-браузерный признак. Сервер здесь настоящий —
/// <c>openssl s_server</c>: он же единственная проверка корректности binder'а, потому что неверный
/// binder сервер молча отвергает и рукопожатие идёт как полное. Проверка пропускается, если
/// openssl в системе нет.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Tls13SessionResumptionTests
{
    private const int TestTimeoutMs = 60000;

    [Test]
    public async Task SecondConnectionResumesWithTicketFromTheFirst()
    {
        using var server = await LocalTlsServer.StartAsync();

        // Первое соединение — полное рукопожатие, из него забираем билет.
        // ★ Именно await using, а не using: поток освобождается только асинхронно, и не закрытое
        // вовремя соединение держит итеративный s_server в себе — второе подключение зависает.
        Tls13SessionTicket? ticket = null;
        await using (var stream = await ConnectAsync(server.Port, pskOffer: null))
        {
            Assert.That(stream.ResumptionAccepted, Is.False, "первое рукопожатие не может быть возобновлением");

            stream.SessionTicketReceived += received => ticket = received;
            await ExchangeAsync(stream, TestContext.CurrentContext.CancellationToken);

            await WaitForTicketAsync(stream, TestContext.CurrentContext.CancellationToken);
        }

        Assert.That(ticket, Is.Not.Null, "сервер не выдал билет сессии после полного рукопожатия");

        // Второе соединение — с предложением PSK: сервер обязан подтвердить ресумпцию.
        using var resumed = await ConnectAsync(server.Port, ticket!.ToOffer());

        Assert.That(resumed.ResumptionAccepted, Is.True, "сервер не принял предложенный билет");
        await ExchangeAsync(resumed, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task RejectedTicketFallsBackToFullHandshake()
    {
        // ★ Главная опасность PSK-пути: сервер, отвергший билет, продолжит ПОЛНОЕ рукопожатие,
        // и клиент обязан корректно его пройти. Ломаный идентификатор сервер гарантированно не
        // примет, но соединение обязано состояться.
        using var server = await LocalTlsServer.StartAsync();

        var offer = new Tls13PskOffer
        {
            Identity = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            PreSharedKey = new byte[32],
            TicketAgeAdd = 42,
            Hash = System.Security.Cryptography.HashAlgorithmName.SHA256,
        };

        using var stream = await ConnectAsync(server.Port, offer);

        Assert.That(stream.ResumptionAccepted, Is.False, "сервер не может принять выдуманный билет");
        await ExchangeAsync(stream, TestContext.CurrentContext.CancellationToken);
    }

    private static async Task<Tls13Stream> ConnectAsync(int port, Tls13PskOffer? pskOffer)
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        var settings = profile.Tls with
        {
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            Extensions = [.. profile.Tls.Extensions.Select(WithHostName)],
            PskOffer = pskOffer,
        };

        var tcp = new TcpStream(new TcpSettings());
        await tcp.ConnectAsync("127.0.0.1", port, TestContext.CurrentContext.CancellationToken);

        var stream = new Tls13Stream(tcp, settings);
        await stream.HandshakeAsync(TestContext.CurrentContext.CancellationToken);
        System.IO.File.AppendAllText("/tmp/ch_dump.log", $"offer={(pskOffer is null ? "none" : "psk")} accepted={stream.ResumptionAccepted}\n");

        return stream;
    }

    /// <summary>
    /// Обменивается HTTP-запросом с <c>s_server -www</c>: доказательство, что обмен данными идёт
    /// под ключами именно этого рукопожатия — полного или возобновлённого.
    /// </summary>
    private static async Task ExchangeAsync(Tls13Stream stream, CancellationToken cancellationToken)
    {
        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"u8;
        await stream.WriteAsync(request.ToArray(), cancellationToken);

        var buffer = new byte[512];
        var total = 0;

        while (total < 32)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read is 0) break;
            total += read;
        }

        Assert.That(total, Is.GreaterThan(0), "сервер не ответил на запрос под ключами рукопожатия");
    }

    /// <summary>
    /// Ждёт, пока сервер пришлёт билет: он уходит после рукопожатия отдельной записью, часто
    /// вперемешку с прикладными данными ответа.
    /// </summary>
    private static async Task WaitForTicketAsync(Tls13Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read is 0) break;
        }
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "localhost" }
            : extension;

    /// <summary>
    /// Локальный сервер TLS 1.3, выдающий и принимающий билеты сессии.
    /// </summary>
    private sealed class LocalTlsServer : IDisposable
    {
        private Process? process;

        public int Port { get; private init; }

        public static async Task<LocalTlsServer> StartAsync()
        {
            var certificate = CreateCertificate();
            var port = FreePort();

            var start = new ProcessStartInfo("openssl")
            {
                ArgumentList =
                {
                    "s_server", "-accept", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-cert", certificate.Certificate, "-key", certificate.Key,
                    "-tls1_3", "-alpn", "h2", "-www", "-msg",
                },
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            Process? started;

            try
            {
                started = Process.Start(start);
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                Assert.Ignore("openssl в системе не найден");
                throw;
            }

            // Сервер поднимается не мгновенно; ждём, пока порт начнёт принимать.
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(100, TestContext.CurrentContext.CancellationToken);

                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(IPAddress.Loopback, port, TestContext.CurrentContext.CancellationToken);
                    break;
                }
                catch (SocketException)
                {
                    // Ещё не слушает.
                }
            }

            return new LocalTlsServer { Port = port, process = started };
        }

        private static (string Certificate, string Key) CreateCertificate()
        {
            using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            var directory = Path.Combine(Path.GetTempPath(), "atom-net-resumption");
            Directory.CreateDirectory(directory);

            var certificatePath = Path.Combine(directory, "cert.pem");
            var keyPath = Path.Combine(directory, "key.pem");

            File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

            return (certificatePath, keyPath);
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();

            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose()
        {
            if (process is null) return;

            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершился.
            }

            process.Dispose();
            process = null;
        }
    }
}
