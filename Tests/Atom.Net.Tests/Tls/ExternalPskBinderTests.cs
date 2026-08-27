using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>Регрессия внешнего PSK: binder проверяется сервером с ИЗВЕСТНЫМ ключом (s_server -psk), поэтому тест ловит порчу вычисления binder'а детерминированно.</summary>
[CancelAfter(60000)]
public sealed class ExternalPskBinderTests
{
    [Test]
    public async Task BinderVerifiesAgainstServerWithKnownPsk()
    {
        using var server = await LocalPskServer.Start();

        var settings = new TlsSettings
        {
            MaxVersion = System.Security.Authentication.SslProtocols.Tls13,
            CipherSuites = [CipherSuite.TLS_AES_128_GCM_SHA256],
            Extensions =
            [
                new ServerNameTlsExtension { HostName = "localhost" },
                new SupportedVersionsTlsExtension { Versions = [System.Security.Authentication.SslProtocols.Tls13, System.Security.Authentication.SslProtocols.Tls12] },
                new Atom.Net.Tls.Extensions.SupportedGroupsTlsExtension { Groups = [NamedGroup.X25519] },
                new Atom.Net.Tls.Extensions.KeyShareTlsExtension { Entries = [KeyShare.ForGroup(NamedGroup.X25519)] },
                new Atom.Net.Tls.Extensions.SignatureAlgorithmsTlsExtension { Algorithms = [Atom.Net.Tls.Extensions.SignatureAlgorithm.RsaPssRsaeSha256] },
            ],
            CheckCertificateRevocationList = false,
            ServerCertificateValidationCallback = static (_, _, _) => true,
            PskOffer = new Tls13PskOffer
            {
                Identity = "test"u8.ToArray(),
                PreSharedKey = Convert.FromHexString("6162636465"),
                TicketAgeAdd = 0,
                Hash = System.Security.Cryptography.HashAlgorithmName.SHA256,
                External = true,
            },
        };

        var tcp = new TcpStream(new TcpSettings());
        await tcp.ConnectAsync("127.0.0.1", server.Port, TestContext.CurrentContext.CancellationToken);

        await using var stream = new Tls13Stream(tcp, settings);
        await stream.HandshakeAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(stream.ResumptionAccepted, Is.True, "сервер не принял внешний PSK — binder не совпал");
    }

    private sealed class LocalPskServer : IDisposable
    {
        private Process? process;
        public int Port { get; private init; }

        public static async Task<LocalPskServer> Start()
        {
            var port = FreePort();
            var start = new ProcessStartInfo("openssl")
            {
                ArgumentList =
                {
                    "s_server", "-accept", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-tls1_3", "-nocert", "-psk", "6162636465", "-psk_identity", "test", "-www", "-quiet",
                },
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
            };
            var process = Process.Start(start)!;

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

            return new LocalPskServer { Port = port, process = process };
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public void Dispose()
        {
            try { if (process is not null && !process.HasExited) process.Kill(true); } catch { }
        }
    }
}
