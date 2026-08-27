using System.Linq;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Закрепляет состав ClientHello профиля Chrome — то, по чему сервер строит отпечаток.
/// </summary>
/// <remarks>
/// Эталон снят с настоящего Chrome 151 и сверен по JA3, JA4 и отпечатку HTTP/2: все три совпали
/// побайтно. Эти проверки не о работоспособности — соединение остаётся рабочим и при любом другом
/// составе. Они о том, что ЛЮБАЯ правка профиля, меняющая отпечаток, будет замечена здесь, а не
/// на боевом сервисе месяц спустя, где она проявится молчаливыми отказами.
///
/// Порядок расширений намеренно НЕ проверяется: начиная с Chrome 110 браузер перемешивает его на
/// каждое соединение, поэтому решающим является состав. По той же причине устойчив JA4, который
/// расширения сортирует, а JA3 у современного Chrome «плавает».
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class ChromeFingerprintParityTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Наборы шифров Chrome 151 в порядке отправки.</summary>
    private static readonly ushort[] ChromeCipherSuites =
    [
        0x1301, 0x1302, 0x1303,
        0xC02B, 0xC02F, 0xC02C, 0xC030, 0xCCA9, 0xCCA8,
        0xC013, 0xC014, 0x009C, 0x009D, 0x002F, 0x0035,
    ];

    /// <summary>Типы расширений Chrome 151 без учёта GREASE и порядка.</summary>
    private static readonly ushort[] ChromeExtensionIds =
    [
        0x0000, 0x0005, 0x000A, 0x000B, 0x000D, 0x0010, 0x0012, 0x001B,
        0x0017, 0x0023, 0x002B, 0x002D, 0x0033, 0x44CD, 0xFE0D, 0xFF01,
    ];

    private static readonly NamedGroup[] ChromeGroups =
    [
        NamedGroup.X25519MLKem768,
        NamedGroup.X25519,
        NamedGroup.Secp256r1,
        NamedGroup.Secp384r1,
    ];

    [Test]
    public void CipherSuitesMatchChrome()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var suites = profile.Tls.CipherSuites.Select(static suite => (ushort)suite).ToArray();

        Assert.That(suites, Is.EqualTo(ChromeCipherSuites).AsCollection);
    }

    [Test]
    public void ExtensionSetMatchesChrome()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        // GREASE исключаем: его значение выводится из random конкретного ClientHello и потому в
        // профиле ещё не определено.
        var ids = profile.Tls.Extensions
            .Where(static extension => extension is not GreaseTlsExtension)
            .Select(static extension => extension.Id)
            .OrderBy(static id => id)
            .ToArray();

        Assert.That(ids, Is.EqualTo(ChromeExtensionIds.OrderBy(static id => id).ToArray()).AsCollection);
    }

    [Test]
    public void GreaseFramesTheExtensionList()
    {
        // Браузер вставляет GREASE первым и последним. Две штуки, а не одна: значения должны
        // различаться, иначе в списке появится повторяющийся тип расширения.
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var extensions = profile.Tls.Extensions.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(extensions[0], Is.InstanceOf<GreaseTlsExtension>());
            Assert.That(extensions[^1], Is.InstanceOf<GreaseTlsExtension>());
            Assert.That(extensions.Count(static extension => extension is GreaseTlsExtension), Is.EqualTo(2));
            Assert.That(((GreaseTlsExtension)extensions[0]).Slot, Is.Not.EqualTo(((GreaseTlsExtension)extensions[^1]).Slot));
        });
    }

    [Test]
    public void SupportedGroupsLeadWithPostQuantumHybrid()
    {
        Assert.That(KeyShare.IsMLKemSupported, Is.True, "платформа не предоставляет ML-KEM");

        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var groups = profile.Tls.Extensions.OfType<SupportedGroupsTlsExtension>().Single().Groups.ToArray();

        Assert.That(groups, Is.EqualTo(ChromeGroups).AsCollection);
    }

    [Test]
    public void KeyShareCarriesHybridAndClassicEntries()
    {
        // Браузер отправляет доли сразу для двух групп, чтобы сервер завершил обмен за один круг
        // независимо от своего выбора.
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var entries = profile.Tls.Extensions.OfType<KeyShareTlsExtension>().Single().Entries.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Length.EqualTo(2));
            Assert.That(entries[0].Group, Is.EqualTo(NamedGroup.X25519MLKem768));
            Assert.That(entries[0].PublicKey.Length, Is.EqualTo(1216), "доля гибрида — 1184 байта ML-KEM плюс 32 байта X25519");
            Assert.That(entries[1].Group, Is.EqualTo(NamedGroup.X25519));
            Assert.That(entries[1].PublicKey.Length, Is.EqualTo(32));
        });
    }

    [Test]
    public void EphemeralKeysAreFreshForEveryProfile()
    {
        // Профиль нельзя кешировать: повторное использование эфемерного ключа не только ослабляет
        // защиту, но и само по себе аномально — у браузера доля ключа новая на каждое соединение.
        var first = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var second = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        var firstShare = first.Tls.Extensions.OfType<KeyShareTlsExtension>().Single().Entries.First().PublicKey;
        var secondShare = second.Tls.Extensions.OfType<KeyShareTlsExtension>().Single().Entries.First().PublicKey;

        Assert.That(firstShare.Span.SequenceEqual(secondShare.Span), Is.False);
    }

    [Test]
    public void SignatureAlgorithmsMatchChrome()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var algorithms = profile.Tls.Extensions.OfType<SignatureAlgorithmsTlsExtension>().Single().Algorithms.Select(static value => (ushort)value).ToArray();

        ushort[] expected =
        [
            0x0904, 0x0905, 0x0906,
            0x0403, 0x0804, 0x0401,
            0x0503, 0x0805, 0x0501,
            0x0806, 0x0601,
        ];

        Assert.That(algorithms, Is.EqualTo(expected).AsCollection);
    }

    [Test]
    public void ApplicationSettingsAnnounceHttp2()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var settings = profile.Tls.Extensions.OfType<ApplicationSettingsTlsExtension>().Single();

        // Тело повторяет форму ALPN: длина списка(2) + длина имени(1) + «h2».
        byte[] expected = [0x00, 0x03, 0x02, (byte)'h', (byte)'2'];

        Assert.Multiple(() =>
        {
            Assert.That(settings.Id, Is.EqualTo(0x44CD));
            Assert.That(settings.Data.ToArray(), Is.EqualTo(expected).AsCollection);
        });
    }

    [Test]
    public void CertificateCompressionAnnouncesBrotli()
    {
        // Объявив сжатие, обязаны уметь его разбирать: сервер пришлёт CompressedCertificate
        // вместо Certificate, и рукопожатие оборвётся на проверке подписи.
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var compression = profile.Tls.Extensions.OfType<CompressCertificateTlsExtension>().Single();

        Assert.That(compression.Algorithms, Is.EqualTo(new[] { CertificateCompressionAlgorithm.Brotli }).AsCollection);
    }

    [Test]
    public void EncryptedClientHelloLooksLikeRealMessage()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();
        var ech = profile.Tls.Extensions.OfType<EncryptedClientHelloTlsExtension>().Single();
        var data = ech.Data.ToArray();

        var messageType = data[0];
        var kdf = (data[1] << 8) | data[2];
        var aead = (data[3] << 8) | data[4];
        var senderKeyLength = (data[6] << 8) | data[7];

        Assert.Multiple(() =>
        {
            Assert.That(ech.Id, Is.EqualTo(0xFE0D));
            Assert.That(messageType, Is.Zero, "тип «внешнее сообщение»");
            Assert.That(kdf, Is.EqualTo(0x0001), "HKDF-SHA256");
            Assert.That(aead, Is.EqualTo(0x0001), "AES-128-GCM");
            Assert.That(senderKeyLength, Is.EqualTo(32), "ключ отправителя X25519");
        });
    }

    [Test]
    public void RenegotiationInfoReplacesSignallingCipherSuite()
    {
        // По RFC 5746 клиент сообщает о поддержке пересогласования ЛИБО расширением, ЛИБО
        // сигнальным набором. Браузер отправляет расширение, поэтому набора 0x00FF быть не должно.
        var profile = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Tls.Extensions.OfType<RenegotiationInfoTlsExtension>().Any(), Is.True);
            Assert.That(profile.Tls.CipherSuites.Any(static suite => (ushort)suite is 0x00FF), Is.False);
        });
    }
}
