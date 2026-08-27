using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет сверку имени хоста с сертификатом сервера.
/// </summary>
/// <remarks>
/// Прежняя проверка брала из SAN ТОЛЬКО ПЕРВОЕ имя — через
/// <c>GetNameInfo(X509NameType.DnsName)</c>, который больше одного и не возвращает. Для
/// мультидоменных сертификатов это отказ на исправном сервере, причём типовой: универсальный
/// сертификат CDN несёт <c>[*.example.com, example.com]</c>, и обращение к голому
/// <c>example.com</c> не совпадало с шаблоном, потому что шаблон покрывает только поддомены.
///
/// Вторая половина проверок — про обратное: сертификат НЕ должен приниматься там, где не должен.
/// Прежний код при наличии SAN всё равно смотрел в CN, то есть принимал сертификат с подходящим
/// CN даже тогда, когда SAN явно перечислял другие имена. RFC 6125 §6.4.4 это запрещает, и
/// браузеры при наличии SAN в CN не смотрят вовсе.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HostnameVerificationTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public void NameBeyondTheFirstEntryOfSubjectAlternativeNameMatches()
    {
        // Ровно тот случай, который отвергался: имя стоит в списке вторым.
        using var certificate = CreateCertificate("CN=cdn", ["*.example.com", "example.com", "www.example.com"]);

        Assert.Multiple(() =>
        {
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "example.com"), Is.True);
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "www.example.com"), Is.True);
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "api.example.com"), Is.True, "покрыт шаблоном");
        });
    }

    [Test]
    public void NameOutsideSubjectAlternativeNameIsRejected()
    {
        using var certificate = CreateCertificate("CN=cdn", ["example.com", "example.org"]);

        Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "attacker.test"), Is.False);
    }

    [Test]
    public void CommonNameIsIgnoredWhenSubjectAlternativeNameIsPresent()
    {
        // ★ Сертификат выдан для example.com, но CN назван именем жертвы. Прежний запасной путь
        // на CN принимал такой сертификат для victim.test — то есть проверка имени обходилась.
        using var certificate = CreateCertificate("CN=victim.test", ["example.com"]);

        Assert.Multiple(() =>
        {
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "victim.test"), Is.False);
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "example.com"), Is.True);
        });
    }

    [Test]
    public void CommonNameStillWorksWhenThereIsNoSubjectAlternativeName()
    {
        // Старые сертификаты без SAN встречаются во внутренних контурах; отвергать их незачем.
        using var certificate = CreateCertificate("CN=legacy.test", dnsNames: []);

        Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "legacy.test"), Is.True);
    }

    [Test]
    public void AddressLiteralIsMatchedAgainstAddressEntries()
    {
        using var certificate = CreateCertificate("CN=node", ["node.test"], addresses: [IPAddress.Parse("10.1.2.3")]);

        Assert.Multiple(() =>
        {
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "10.1.2.3"), Is.True);
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "10.1.2.4"), Is.False);
        });
    }

    [Test]
    public void WildcardCoversOnlyOneLabel()
    {
        // *.example.com покрывает api.example.com, но не deep.api.example.com и не сам домен,
        // если тот не перечислен отдельно. Ослабление здесь означало бы принятый чужой сертификат.
        using var certificate = CreateCertificate("CN=cdn", ["*.example.com"]);

        Assert.Multiple(() =>
        {
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "api.example.com"), Is.True);
            Assert.That(ServerCertificateVerifier.HostnameMatches(certificate, "example.com"), Is.False);
        });
    }

    private static X509Certificate2 CreateCertificate(string subject, string[] dnsNames, IPAddress[]? addresses = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);

        if (dnsNames.Length > 0 || addresses is { Length: > 0 })
        {
            var builder = new SubjectAlternativeNameBuilder();

            foreach (var name in dnsNames) builder.AddDnsName(name);
            foreach (var address in addresses ?? []) builder.AddIpAddress(address);

            request.CertificateExtensions.Add(builder.Build());
        }

        return request.CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(50));
    }
}
