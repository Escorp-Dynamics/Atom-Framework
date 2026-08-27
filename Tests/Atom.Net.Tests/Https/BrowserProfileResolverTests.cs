using System.Security.Authentication;
using Atom.Net.Https;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Https;

[TestFixture]
public sealed class BrowserProfileResolverTests
{
    [Test]
    public void CatalogCreatesChromeLinuxProfile()
    {
        var profile = BrowserProfileCatalog.CreateChromeDesktopLinux();

        Assert.Multiple(() =>
        {
            Assert.That(profile.DisplayName, Does.StartWith("Chrome Desktop Linux"));
            Assert.That(profile.UserAgent, Does.Contain("Linux x86_64"));
            Assert.That(profile.Tcp.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(profile.Tls.HandshakeTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public void ResolverMapsEdgeUserAgentToEdgeProfile()
    {
        var profile = BrowserProfileResolver.Resolve("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(profile.DisplayName, Does.StartWith("Edge Desktop Windows"));
            Assert.That(profile.UserAgent, Does.Contain("Edg/131.0.0.0"));
        });
    }

    [Test]
    public void ResolverFallsBackToChromeLinuxWhenUserAgentIsMissing()
    {
        var profile = BrowserProfileResolver.Resolve((string?)null);

        Assert.That(profile.DisplayName, Does.StartWith("Chrome Desktop Linux"));
    }

    [Test]
    public void UserAgentAdapterCreatesHandlerWithResolvedProfile()
    {
        var adapter = new UserAgentAdapter();

        var handler = adapter.CreateHandler("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(handler.BrowserProfile?.DisplayName, Does.StartWith("Edge Desktop Windows"));
            Assert.That(handler.BrowserProfile?.UserAgent, Does.Contain("Edg/131.0.0.0"));
        });
    }

    [Test]
    public void CatalogCreatesDistinctTlsPreferencesForDifferentBrowserFamilies()
    {
        var chromium = BrowserProfileCatalog.CreateChromeDesktopWindows();
        var firefox = BrowserProfileCatalog.CreateFirefoxDesktop();
        var safari = BrowserProfileCatalog.CreateSafariDesktopMacOs();

        var chromiumSignatureAlgorithms = chromium.Tls.Extensions.OfType<SignatureAlgorithmsTlsExtension>().Single().Algorithms.ToArray();
        var firefoxSignatureAlgorithms = firefox.Tls.Extensions.OfType<SignatureAlgorithmsTlsExtension>().Single().Algorithms.ToArray();
        var safariSignatureAlgorithms = safari.Tls.Extensions.OfType<SignatureAlgorithmsTlsExtension>().Single().Algorithms.ToArray();
        var chromiumGroups = chromium.Tls.Extensions.OfType<SupportedGroupsTlsExtension>().Single().Groups.ToArray();
        var firefoxGroups = firefox.Tls.Extensions.OfType<SupportedGroupsTlsExtension>().Single().Groups.ToArray();
        var safariGroups = safari.Tls.Extensions.OfType<SupportedGroupsTlsExtension>().Single().Groups.ToArray();
        var chromiumExtensionIds = chromium.Tls.Extensions.Select(static extension => extension.Id).ToArray();
        var firefoxExtensionIds = firefox.Tls.Extensions.Select(static extension => extension.Id).ToArray();
        // GREASE пропускаем: его номер выводится из random конкретного сообщения и в профиле ещё
        // не определён.
        var safariExtensionIds = safari.Tls.Extensions
            .Where(static extension => extension is not GreaseTlsExtension)
            .Select(static extension => extension.Id)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(chromium.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256));
            Assert.That(firefox.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_AES_128_GCM_SHA256));
            Assert.That(safari.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_AES_128_GCM_SHA256), "замер Safari 18.3: наборы TLS 1.3 идут первыми");
            Assert.That(chromiumSignatureAlgorithms, Contains.Item(SignatureAlgorithm.RsaPkcs1Sha1));
            Assert.That(firefoxSignatureAlgorithms, Does.Not.Contain(SignatureAlgorithm.Ed25519), "NSS не предлагает ed25519 в подписях рукопожатия");
            Assert.That(safariSignatureAlgorithms, Does.Not.Contain(SignatureAlgorithm.Ed25519));
            Assert.That(chromiumGroups.Take(3), Is.EqualTo(new[] { NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1 }));
            Assert.That(firefoxGroups.Last(), Is.EqualTo(NamedGroup.Ffdhe3072));
            Assert.That(safariGroups.First(), Is.EqualTo(NamedGroup.X25519), "замер Safari 18.3: X25519 впереди кривых NIST");
            Assert.That(chromiumExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x0017, 0xff01, 0x000A, 0x000B, 0x0023, 0x0010, 0x000D, 0x002B }));
            Assert.That(firefoxExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x0017, 0xff01, 0x000A, 0x000B, 0x0023, 0x0010, 0x0005, 0x0022, 0x0012, 0x0033, 0x002B, 0x000D, 0x002D, 0x001C, 0x001B, 0xfe0d }));
            Assert.That(safariExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x0017, 0xff01, 0x000A, 0x000B, 0x0010, 0x0005, 0x000D, 0x0012, 0x0033, 0x002D, 0x002B, 0x001B, 0x0015 }));
        });
    }

    [Test]
    public void CatalogUsesStrictOriginWhenCrossOriginAsDefaultReferrerPolicy()
    {
        var chromium = BrowserProfileCatalog.CreateChromeDesktopWindows();
        var firefox = BrowserProfileCatalog.CreateFirefoxDesktop();
        var safari = BrowserProfileCatalog.CreateSafariDesktopMacOs();

        Assert.Multiple(() =>
        {
            Assert.That(chromium.Headers.DefaultReferrerPolicy, Is.EqualTo(ReferrerPolicyMode.StrictOriginWhenCrossOrigin));
            Assert.That(firefox.Headers.DefaultReferrerPolicy, Is.EqualTo(ReferrerPolicyMode.StrictOriginWhenCrossOrigin));
            Assert.That(safari.Headers.DefaultReferrerPolicy, Is.EqualTo(ReferrerPolicyMode.StrictOriginWhenCrossOrigin));
        });
    }

    [Test]
    public void CatalogKeepsHeaderDefaultsFamilyAware()
    {
        var chromium = BrowserProfileCatalog.CreateChromeDesktopWindows();
        var firefox = BrowserProfileCatalog.CreateFirefoxDesktop();
        var safari = BrowserProfileCatalog.CreateSafariDesktopMacOs();

        Assert.Multiple(() =>
        {
            Assert.That(chromium.Headers.UseClientHints, Is.True);
            Assert.That(firefox.Headers.UseClientHints, Is.False);
            Assert.That(safari.Headers.UseClientHints, Is.False);
            Assert.That(chromium.Headers.DefaultRequestKind, Is.EqualTo(RequestKind.Fetch));
            Assert.That(firefox.Headers.DefaultRequestKind, Is.EqualTo(RequestKind.Fetch));
            Assert.That(safari.Headers.DefaultRequestKind, Is.EqualTo(RequestKind.Fetch));
        });
    }

    /// <summary>Строки агента и ожидаемые от них профили.</summary>
    private static IEnumerable<TestCaseData> AgentStrings()
    {
        yield return new TestCaseData(
            "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36",
            "Chrome Android").SetName("{m}(Chrome Android)");

        yield return new TestCaseData(
            "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36 EdgA/151.0.0.0",
            "Edge Android").SetName("{m}(Edge Android)");

        yield return new TestCaseData(
            "Mozilla/5.0 (Linux; Android 15; SAMSUNG SM-S928B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/27.0 Chrome/151.0.0.0 Mobile Safari/537.36",
            "Samsung Internet").SetName("{m}(Samsung Internet)");

        yield return new TestCaseData(
            "Mozilla/5.0 (Android 15; Mobile; rv:154.0) Gecko/154.0 Firefox/154.0",
            "Firefox Android").SetName("{m}(Firefox Android)");

        yield return new TestCaseData(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1",
            "Safari iOS").SetName("{m}(Safari iOS)");

        yield return new TestCaseData(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/151.0.0.0 Mobile/15E148 Safari/604.1",
            "Chrome iOS").SetName("{m}(Chrome iOS)");
    }

    [TestCaseSource(nameof(AgentStrings))]
    public void MobileAgentsResolveToMobileProfiles(string userAgent, string expectedName)
    {
        // Мобильных строк резолвер не знал вовсе: телефон получал настольный профиль, то есть
        // заявлял Android и вёл себя как настольная система — сочетание, которого не бывает.
        var profile = BrowserProfileResolver.Resolve(userAgent);

        Assert.Multiple(() =>
        {
            Assert.That(profile.DisplayName, Does.StartWith(expectedName));
            Assert.That(profile.IsMobile, Is.True);
        });
    }

    [Test]
    public void ChromiumOnIosGetsTheWebKitTransport()
    {
        // ★ На iOS сторонним браузерам запрещён собственный движок. Хромиумовский отпечаток со
        // строкой агента CriOS — сочетание, которого на устройстве не существует, и заметить его
        // проще, чем любую отдельную мелочь.
        var chromeIos = BrowserProfileResolver.Resolve(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/151.0.0.0 Mobile/15E148 Safari/604.1");

        var safariIos = BrowserProfileCatalog.CreateSafariIos();

        Assert.That(chromeIos.Tls.CipherSuites, Is.EqualTo(safariIos.Tls.CipherSuites).AsCollection);
    }

    [TestCase("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36")]
    [TestCase("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0")]
    [TestCase("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36")]
    [TestCase(null)]
    public void DesktopChromiumResolvesToTheMeasuredProfile(string? userAgent)
    {
        // ★ Резолвер выдавал профили эпохи TLS 1.2 — написанные по памяти, с девятью
        // расширениями, — тогда как рядом лежали снятые с настоящего Chrome 151 и совпадающие с
        // ним побайтно. Отпечаток оставался рабочим, просто принадлежал не тому браузеру.
        var profile = BrowserProfileResolver.Resolve(userAgent);
        var measured = BrowserProfileCatalog.CreateChromeDesktopWindowsTls13();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Tls.MaxVersion.HasFlag(SslProtocols.Tls13), Is.True);
            Assert.That(profile.Tls.CipherSuites, Is.EqualTo(measured.Tls.CipherSuites).AsCollection);
            Assert.That(profile.IsMobile, Is.False);
        });
    }

    [Test]
    public void EdgeIsRecognisedBeforeChrome()
    {
        // Порядок проверок важнее их содержания: строка Edge несёт и Chrome/, и Safari/, поэтому
        // проверять надо от частного к общему, иначе всё сведётся к одному профилю.
        var edge = BrowserProfileResolver.Resolve(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

        Assert.That(edge.UserAgent, Does.Contain("Edg/"));
    }
}
