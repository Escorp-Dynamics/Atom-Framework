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
/// Проверяет ответ на HelloRetryRequest против настоящего сервера.
/// </summary>
/// <remarks>
/// ★ Случай наступает, когда сервер предпочитает группу, которую мы объявили в
/// <c>supported_groups</c>, но долю ключа для неё заранее не отправили. Профиль Firefox объявляет
/// P-521 и конечнополевые группы, Chrome — P-384, а доли уходят только для гибрида, X25519 и
/// P-256: значит, повтор приветствия — не экзотика, а штатный путь к части серверов.
///
/// Прежде он не поддерживался вовсе и приходил как «ServerHello без key_share»: в
/// HelloRetryRequest расширение key_share несёт ТОЛЬКО номер группы, без самой доли, и разбор
/// спотыкался именно на этом. Причина по такому сообщению не угадывается.
///
/// Сервер здесь настоящий — <c>openssl s_server</c>, ограниченный одной группой. Проверка
/// пропускается, если openssl в системе нет.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HelloRetryRequestTests
{
    private const int TestTimeoutMs = 60000;

    [TestCase("P-384", "chrome", Description = "Chrome объявляет P-384, но доли для неё не шлёт")]
    [TestCase("P-521", "firefox", Description = "Firefox объявляет P-521, но доли для неё не шлёт")]
    [TestCase("P-384", "firefox")]
    public async Task RetryCompletesTheHandshake(string group, string browser)
    {
        using var server = await LocalTlsServer.StartAsync(group);

        Assert.That(await HandshakeAsync(server.Port, browser), Is.EqualTo("h2"));
    }

    [TestCase("X25519", "chrome", Description = "доля отправлена сразу — повтор не нужен")]
    [TestCase("X25519", "firefox")]
    public async Task DirectHandshakeStillWorks(string group, string browser)
    {
        // Страховка от главной опасности такой правки: путь БЕЗ повтора обязан остаться прежним.
        using var server = await LocalTlsServer.StartAsync(group);

        Assert.That(await HandshakeAsync(server.Port, browser), Is.EqualTo("h2"));
    }

    [Test]
    public async Task FiniteFieldGroupsFailWithAClearMessage()
    {
        // Конечнополевые группы намеренно не поддержаны: в открытой сети их не выбирает никто, а
        // возведение в степень на трёх тысячах бит стоило бы дороже всего рукопожатия. Важно,
        // чтобы отказ НАЗЫВАЛ причину, а не обрывался молча.
        using var server = await LocalTlsServer.StartAsync("ffdhe2048");

        Assert.That(
            async () => await HandshakeAsync(server.Port, "firefox"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("Ffdhe2048"));
    }

    private static async Task<string?> HandshakeAsync(int port, string browser)
    {
        var profile = browser is "firefox"
            ? BrowserProfileCatalog.CreateFirefoxDesktop()
            : BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        var settings = profile.Tls with
        {
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            Extensions = [.. profile.Tls.Extensions.Select(WithHostName)],
        };

        var tcp = new TcpStream(new TcpSettings());
        await tcp.ConnectAsync("127.0.0.1", port, TestContext.CurrentContext.CancellationToken);

        await using var stream = new Tls13Stream(tcp, settings);
        await stream.HandshakeAsync(TestContext.CurrentContext.CancellationToken);

        return stream.NegotiatedProtocol;
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "localhost" }
            : extension;

    /// <summary>
    /// Локальный сервер TLS 1.3, ограниченный одной группой.
    /// </summary>
    private sealed class LocalTlsServer : IDisposable
    {
        private Process? process;

        public int Port { get; private init; }

        public static async Task<LocalTlsServer> StartAsync(string group)
        {
            var certificate = CreateCertificate();
            var port = FreePort();

            var start = new ProcessStartInfo("openssl")
            {
                ArgumentList =
                {
                    "s_server", "-accept", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-cert", certificate.Certificate, "-key", certificate.Key,
                    "-tls1_3", "-groups", group, "-alpn", "h2", "-www", "-quiet",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            var directory = Path.Combine(Path.GetTempPath(), "atom-net-hrr");
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
