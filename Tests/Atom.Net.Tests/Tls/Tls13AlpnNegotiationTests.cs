using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет, что результат согласования ALPN доходит до вызывающей стороны.
/// </summary>
/// <remarks>
/// Проверка выглядит мелкой, а её отсутствие стоило скрытой поломки всего пути HTTP/2 поверх
/// TLS 1.3. Разбор EncryptedExtensions живёт в движке рукопожатия, а выбор класса соединения
/// делается по свойству ПОТОКА. Стоит связи между ними порваться — и клиент, договорившийся с
/// сервером о <c>h2</c>, начинает разбирать ответ как HTTP/1.1: соединение рвётся без единой
/// внятной ошибки.
///
/// Сервер поднимается локальный, на самоподписанном сертификате: сеть здесь ни при чём,
/// проверяется именно связка «согласовали — сообщили наверх».
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Tls13AlpnNegotiationTests
{
    private const int TestTimeoutMs = 30000;

    [TestCase("h2")]
    [TestCase("http/1.1")]
    public async Task NegotiatedProtocolReachesTheStream(string expected)
    {
        using var certificate = CreateSelfSignedCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);

        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunServerAsync(listener, certificate, expected);

        using var socket = new TcpStream(new TcpSettings { IsNagleDisabled = true, ConnectTimeout = TimeSpan.FromSeconds(10) });
        await socket.ConnectAsync("127.0.0.1", port, TestContext.CurrentContext.CancellationToken);

        await using var tls = new Tls13Stream(socket, CreateClientSettings(expected));
        await tls.HandshakeAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(tls.NegotiatedProtocol, Is.EqualTo(expected));

        await serverTask;
    }

    private static TlsSettings CreateClientSettings(string protocol)
        => new()
        {
            MinVersion = System.Security.Authentication.SslProtocols.Tls13,
            MaxVersion = System.Security.Authentication.SslProtocols.Tls13,
            CipherSuites = [CipherSuite.TLS_AES_128_GCM_SHA256, CipherSuite.TLS_AES_256_GCM_SHA384],
            SessionIdPolicy = SessionIdPolicy.Fixed32,

            // Сертификат самоподписанный: проверять цепочку здесь незачем, проверяется согласование.
            ServerCertificateValidationCallback = static (_, _, _) => true,
            Extensions =
            [
                new ServerNameTlsExtension { HostName = "localhost" },
                new ExtendedMasterSecretTlsExtension { IsEnabled = true },
                new RenegotiationInfoTlsExtension(),
                new SupportedGroupsTlsExtension { Groups = [NamedGroup.X25519, NamedGroup.Secp256r1] },
                new EcPointFormatsTlsExtension { Formats = [0x00] },
                new AlpnTlsExtension { Protocols = [System.Text.Encoding.ASCII.GetBytes(protocol)] },
                new SignatureAlgorithmsTlsExtension
                {
                    Algorithms =
                    [
                        SignatureAlgorithm.RsaPssRsaeSha256,
                        SignatureAlgorithm.RsaPkcs1Sha256,
                        SignatureAlgorithm.EcdsaSecp256r1Sha256,
                        SignatureAlgorithm.RsaPssRsaeSha384,
                    ]
                },
                new KeyShareTlsExtension { Entries = [KeyShare.X25519] },
                new PskKeyExchangeModesTlsExtension { Modes = [PskKeyExchangeMode.PskDheKe] },
                new SupportedVersionsTlsExtension { Versions = [System.Security.Authentication.SslProtocols.Tls13] },
            ],
        };

    private static async Task RunServerAsync(TcpListener listener, X509Certificate2 certificate, string protocol)
    {
        using var client = await listener.AcceptTcpClientAsync(TestContext.CurrentContext.CancellationToken);
        await using var stream = client.GetStream();
        await using var ssl = new SslStream(stream, leaveInnerStreamOpen: false);

        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13,
            ApplicationProtocols = [new SslApplicationProtocol(protocol)],
        }, TestContext.CurrentContext.CancellationToken);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeName.Build());

        var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // На части платформ закрытый ключ доступен серверу только после экспорта и обратного
        // импорта: без этого AuthenticateAsServerAsync отказывается работать с сертификатом.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
    }
}
