using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Https.Profiles;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Взаимная аутентификация TLS 1.3 (клиентские сертификаты) против настоящего сервера.
/// </summary>
/// <remarks>
/// Оракул — <c>openssl s_server -Verify</c>: он требует клиентский сертификат, проверяет цепочку
/// против ca.pem и подпись CertificateVerify. Пропускается, если openssl в системе нет.
/// </remarks>
[CancelAfter(60000)]
public sealed class Tls13ClientCertificateTests
{
    [Test]
    public async Task ClientCertificateIsSentAndAcceptedByServer()
    {
        using var server = await LocalServer.StartAsync();

        await using var stream = await ConnectAsync(server.Port, server.ClientCertificate);
        await ExchangeAsync(stream, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task MissingClientCertificateFailsWhenServerRequiresIt()
    {
        using var server = await LocalServer.StartAsync();

        // Сервер с -Verify обязан оборвать рукопожатие без клиентского сертификата:
        // пустой Certificate — валидный ответ, но допуск решает сервер.
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var stream = await ConnectAsync(server.Port, null, useClientCertificate: false);
            await ExchangeAsync(stream, TestContext.CurrentContext.CancellationToken);
        });
    }

    private static async Task<Tls13Stream> ConnectAsync(int port, X509Certificate2? clientCertificate, bool useClientCertificate = true)
    {
        var profile = ProfileCatalog.CreateChrome();
        var settings = profile.Tls with
        {
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            Extensions = [.. profile.Tls.Extensions.Select(WithHostName)],
            ClientCertificate = useClientCertificate ? clientCertificate : null,
        };

        var tcp = new TcpStream(new TcpSettings());
        await tcp.ConnectAsync("127.0.0.1", port, TestContext.CurrentContext.CancellationToken);

        var stream = new Tls13Stream(tcp, settings);
        await stream.HandshakeAsync(TestContext.CurrentContext.CancellationToken);
        var hs = typeof(Tls13Stream).GetField("handshake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(stream);
        var secretProp = hs!.GetType().GetProperty("ClientHandshakeSecret")!;
        var secret = (ReadOnlyMemory<byte>)secretProp.GetValue(hs)!;
        System.IO.File.AppendAllText("/tmp/mtls_debug.log", Convert.ToHexString(secret.Span) + "\n"); // DEBUG
        return stream;
    }

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

        Assert.That(total, Is.GreaterThan(0), "сервер не ответил после взаимной аутентификации");
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "localhost" }
            : extension;

    private static class ProfileCatalog
    {
        public static BrowserProfile CreateChrome() =>
            BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    private sealed class LocalServer : IDisposable
    {
        private Process? process;
        public int Port { get; private init; }
        public X509Certificate2 ClientCertificate { get; private init; } = null!;

        public static async Task<LocalServer> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "atom-net-mtls");
            Directory.CreateDirectory(directory);

            // CA
            RunOpenssl($"req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -keyout {Q(Path.Combine(directory, "ca.key"))} -out {Q(Path.Combine(directory, "ca.pem"))} -days 2 -nodes -subj /CN=TestCA");
            // Серверный сертификат
            RunOpenssl($"req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -keyout {Q(Path.Combine(directory, "srv.key"))} -out {Q(Path.Combine(directory, "srv.pem"))} -days 2 -nodes -subj /CN=localhost");
            // Клиентский сертификат, подписанный CA
            RunOpenssl($"req -new -newkey ec -pkeyopt ec_paramgen_curve:P-256 -keyout {Q(Path.Combine(directory, "cli.key"))} -out {Q(Path.Combine(directory, "cli.csr"))} -days 2 -nodes -subj /CN=client");
            RunOpenssl($"x509 -req -in {Q(Path.Combine(directory, "cli.csr"))} -CA {Q(Path.Combine(directory, "ca.pem"))} -CAkey {Q(Path.Combine(directory, "ca.key"))} -days 2 -out {Q(Path.Combine(directory, "cli.pem"))}");

            var clientCertificate = X509Certificate2.CreateFromPemFile(
                Path.Combine(directory, "cli.pem"), Path.Combine(directory, "cli.key"));

            var port = FreePort();
            var start = new ProcessStartInfo("openssl")
            {
                ArgumentList =
                {
                    "s_server", "-accept", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-cert", Path.Combine(directory, "srv.pem"), "-key", Path.Combine(directory, "srv.key"),
                    "-tls1_3", "-ciphersuites", "TLS_AES_128_GCM_SHA256", "-alpn", "h2", "-www", "-quiet", "-msg", "-msg", "-keylogfile", "/tmp/kl_" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".log",
                    "-Verify", "1", "-verifyCAfile", Path.Combine(directory, "ca.pem"),
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
            };

            var process = Process.Start(start)!;
            _ = process.StandardOutput.ReadToEndAsync().ContinueWith(t => File.WriteAllText($"/tmp/s_srv_out_{port}.log", t.Result));
            _ = process.StandardError.ReadToEndAsync().ContinueWith(t => File.WriteAllText($"/tmp/s_srv_err_{port}.log", t.Result));

            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(100, TestContext.CurrentContext.CancellationToken);
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(IPAddress.Loopback, port, TestContext.CurrentContext.CancellationToken);
                    break;
                }
                catch (SocketException) { }
            }

            return new LocalServer { Port = port, process = process, ClientCertificate = clientCertificate };
        }

        private static void RunOpenssl(string arguments)
        {
            var result = System.Diagnostics.Process.Start(new ProcessStartInfo("openssl")
            {
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            result!.WaitForExit(30000);
            if (result.ExitCode is not 0) throw new InvalidOperationException("openssl failed: " + arguments);
        }

        private static string Q(string path) => "\"" + path + "\"";

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose()
        {
            if (process is null) return;
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.Dispose();
            process = null;
        }
    }
}
