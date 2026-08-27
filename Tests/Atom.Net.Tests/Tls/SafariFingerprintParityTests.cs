using System.Linq;
using Atom.Net.Https.Http2;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Закрепляет ClientHello профиля Safari сверкой с записанным обменом настоящего браузера.
/// </summary>
/// <remarks>
/// ★ Эталон — слепок Safari 18.3 на iOS, приведённый в обсуждении проекта <c>curl_cffi</c>
/// (запись настоящего обмена, а не описание по памяти):
///
/// <code>
/// ja3_hash 773906b0efdefa24a7f2b8eb6985bf37
/// akamai   2:0;3:100;4:2097152;8:1;9:1|10420225|0|m,s,a,p
/// </code>
///
/// Устройства Apple под рукой нет, поэтому снять замер самим нельзя — и это единственный профиль,
/// проверенный по чужой записи. Зато проверка получилась сильной: ja3 покрывает наборы шифров,
/// порядок расширений и группы разом, а совпадение хэша означает совпадение всех трёх списков
/// побайтно.
///
/// Прежний профиль был догадкой: TLS 1.2, четыре набора шифров, ни GREASE, ни дополнения — то
/// есть Safari, какого не существует.
///
/// Не сверено: алгоритмы подписи (в ja3 они не входят). Взяты из профиля
/// <c>HelloSafari_26_3</c> проекта <c>utls</c>.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class SafariFingerprintParityTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Наборы шифров Safari в порядке отправки.</summary>
    private static readonly ushort[] SafariCipherSuites =
    [
        0x1301, 0x1302, 0x1303,
        0xC02C, 0xC02B, 0xCCA9, 0xC030, 0xC02F, 0xCCA8,
        0xC00A, 0xC009, 0xC014, 0xC013,
        0x009D, 0x009C, 0x0035, 0x002F,
        0xC008, 0xC012, 0x000A,
    ];

    /// <summary>Расширения Safari в порядке отправки, без GREASE.</summary>
    private static readonly ushort[] SafariExtensions =
    [
        0x0000, 0x0017, 0xFF01, 0x000A, 0x000B, 0x0010, 0x0005,
        0x000D, 0x0012, 0x0033, 0x002D, 0x002B, 0x001B, 0x0015,
    ];

    [Test]
    public void CipherSuitesMatchTheCapturedBrowser()
    {
        var profile = BrowserProfileCatalog.CreateSafariDesktopMacOs();
        var suites = profile.Tls.CipherSuites.Select(static suite => (ushort)suite).ToArray();

        Assert.That(suites, Is.EqualTo(SafariCipherSuites).AsCollection);
    }

    [Test]
    public void ExtensionOrderMatchesTheCapturedBrowser()
    {
        var profile = BrowserProfileCatalog.CreateSafariDesktopMacOs();

        var ids = profile.Tls.Extensions
            .Where(static extension => extension is not GreaseTlsExtension)
            .Select(static extension => extension.Id)
            .ToArray();

        Assert.That(ids, Is.EqualTo(SafariExtensions).AsCollection);
    }

    [Test]
    public void SafariAsksForNoSessionTicketButPadsInstead()
    {
        // ★ Отличительная мелочь, на которой профиль и ловился: билета сессии Safari не просит
        // вовсе, а на его месте в списке стоит дополнение. Прежде билет попадал в блок сам —
        // слой соединений дописывал недостающие умолчания в хвост профиля.
        var profile = BrowserProfileCatalog.CreateSafariDesktopMacOs();
        var ids = profile.Tls.Extensions.Select(static extension => extension.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Not.Contain((ushort)0x0023), "билет сессии Safari не запрашивает");
            Assert.That(ids, Contains.Item((ushort)0x0015), "вместо него отправляется дополнение");
        });
    }

    [Test]
    public void GroupsMatchTheCapturedBrowser()
    {
        // Постквантового гибрида в замере нет: Safari 18.3 объявляет только X25519 и кривые NIST.
        var profile = BrowserProfileCatalog.CreateSafariDesktopMacOs();
        var groups = profile.Tls.Extensions.OfType<SupportedGroupsTlsExtension>().Single().Groups.ToArray();

        Assert.That(groups, Is.EqualTo(new[] { NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1, NamedGroup.Secp521r1 }).AsCollection);
    }

    [Test]
    public void Http2SignatureMatchesTheCapturedBrowser()
    {
        // Слепок: 2:0;3:100;4:2097152;8:1;9:1|10420225|0|m,s,a,p
        var settings = Http2ProfileCatalog.CreateSafari();
        var order = settings.SettingsOrder.Select(static setting => (setting.Id, setting.Value)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new (uint, uint)[] { (2, 0), (3, 100), (4, 2097152), (8, 1), (9, 1) }).AsCollection);
            Assert.That(settings.ConnectionWindowIncrement, Is.EqualTo(10_420_225));
            Assert.That(settings.PseudoHeaderOrder, Is.EqualTo("msap"));
            Assert.That(settings.UsePriorityFrames, Is.False);
        });
    }

    [Test]
    public void SafariDiffersFromChromiumWhereItShould()
    {
        // Три различия, каждое наблюдаемо: размера таблицы заголовков Safari не объявляет вовсе,
        // а расширенный CONNECT и отказ от приоритетов RFC 7540 объявляет он один.
        var safari = Http2ProfileCatalog.CreateSafari().SettingsOrder.Select(static setting => setting.Id).ToArray();
        var chrome = Http2ProfileCatalog.CreateChrome().SettingsOrder.Select(static setting => setting.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(safari, Does.Not.Contain(1u), "размер таблицы заголовков Safari не объявляет");
            Assert.That(safari, Contains.Item(8u));
            Assert.That(safari, Contains.Item(9u));
            Assert.That(chrome, Does.Not.Contain(8u));
        });
    }

    [Test]
    public void IosSharesTheDesktopTransport()
    {
        // У Apple одна библиотека TLS на все платформы: отдельного отпечатка у iOS нет, и
        // различаться профили обязаны только прикладной частью.
        var desktop = BrowserProfileCatalog.CreateSafariDesktopMacOs();
        var mobile = BrowserProfileCatalog.CreateSafariIos();

        Assert.Multiple(() =>
        {
            Assert.That(mobile.Tls.CipherSuites, Is.EqualTo(desktop.Tls.CipherSuites).AsCollection);
            Assert.That(mobile.UserAgent, Does.Contain("iPhone"));
            Assert.That(mobile.IsMobile, Is.True);
            Assert.That(desktop.IsMobile, Is.False);
        });
    }

    [Test]
    public void Ja3AndJa4MatchTheCapturedBrowser()
    {
        // Сверка собственным разборщиком, без сети: ClientHello строится тем же кодом, что уходит
        // на провод, и разбирается на месте.
        //
        // ★ Тонкость, стоившая разбирательства: зеркало tls.peet.ws ИСКЛЮЧАЕТ расширение
        // дополнения (0x0015) из хэша JA4, а спецификация FoxIO — включает. Одно и то же
        // сообщение поэтому даёт у них разные третьи части, и обе верны. Здесь закреплено
        // значение ПО СПЕЦИФИКАЦИИ, потому что его считает наш разборщик.
        var inspector = InspectSafariClientHello();

        Assert.Multiple(() =>
        {
            Assert.That(inspector.ComputeJa3Hash(), Is.EqualTo("773906b0efdefa24a7f2b8eb6985bf37"), inspector.ComputeJa3());
            Assert.That(inspector.ComputeJa4(), Is.EqualTo("t13d2014h2_a09f3c656075_e42f34c56612"));
        });
    }

    [Test]
    public void SignatureAlgorithmsRepeatOneEntryExactlyAsSafariDoes()
    {
        // ★ Safari перечисляет rsa_pss_rsae_sha384 ДВАЖДЫ подряд. Это особенность его библиотеки,
        // а не опечатка: без повтора третья часть JA4 не сходится, и именно на этом отпечаток
        // долго не совпадал, пока список считался набором без повторов.
        var algorithms = InspectSafariClientHello().SignatureAlgorithms.ToArray();

        Assert.That(algorithms, Is.EqualTo(new ushort[]
        {
            0x0403, 0x0804, 0x0401, 0x0503, 0x0805, 0x0805, 0x0501, 0x0806, 0x0601, 0x0201,
        }).AsCollection);
    }

    /// <summary>
    /// Строит ClientHello профилем Safari и разбирает его.
    /// </summary>
    /// <returns>Разобранное сообщение.</returns>
    private static ClientHelloInspector InspectSafariClientHello()
    {
        var profile = BrowserProfileCatalog.CreateSafariDesktopMacOs();
        var settings = profile.Tls with { Extensions = [.. profile.Tls.Extensions.Select(WithHostName)] };

        using var handshake = new Tls13ClientHandshake(settings);

        return ClientHelloInspector.Parse(handshake.BuildClientHello());
    }

    private static ITlsExtension WithHostName(ITlsExtension extension)
        => extension is ServerNameTlsExtension serverName
            ? new ServerNameTlsExtension { Id = serverName.Id, HostName = "tls.peet.ws" }
            : extension;
}
