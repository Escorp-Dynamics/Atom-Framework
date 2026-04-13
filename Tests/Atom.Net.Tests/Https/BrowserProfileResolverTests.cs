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
            Assert.That(profile.DisplayName, Is.EqualTo("Chrome Desktop Linux"));
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
            Assert.That(profile.DisplayName, Is.EqualTo("Edge Desktop Windows"));
            Assert.That(profile.UserAgent, Does.Contain("Edg/131.0.0.0"));
        });
    }

    [Test]
    public void ResolverFallsBackToChromeLinuxWhenUserAgentIsMissing()
    {
        var profile = BrowserProfileResolver.Resolve((string?)null);

        Assert.That(profile.DisplayName, Is.EqualTo("Chrome Desktop Linux"));
    }

    [Test]
    public void UserAgentAdapterCreatesHandlerWithResolvedProfile()
    {
        var adapter = new UserAgentAdapter();

        var handler = adapter.CreateHandler("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

        Assert.Multiple(() =>
        {
            Assert.That(handler.BrowserProfile?.DisplayName, Is.EqualTo("Edge Desktop Windows"));
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
        var safariExtensionIds = safari.Tls.Extensions.Select(static extension => extension.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(chromium.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256));
            Assert.That(firefox.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256));
            Assert.That(safari.Tls.CipherSuites.First(), Is.EqualTo(CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384));
            Assert.That(chromiumSignatureAlgorithms, Contains.Item(SignatureAlgorithm.RsaPkcs1Sha1));
            Assert.That(firefoxSignatureAlgorithms, Contains.Item(SignatureAlgorithm.Ed25519));
            Assert.That(safariSignatureAlgorithms, Does.Not.Contain(SignatureAlgorithm.Ed25519));
            Assert.That(chromiumGroups.Take(3), Is.EqualTo(new[] { NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1 }));
            Assert.That(firefoxGroups.Last(), Is.EqualTo(NamedGroup.Secp521r1));
            Assert.That(safariGroups.First(), Is.EqualTo(NamedGroup.Secp256r1));
            Assert.That(chromiumExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x0017, 0xff01, 0x000A, 0x000B, 0x0023, 0x0010, 0x000D, 0x002B }));
            Assert.That(firefoxExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x000A, 0x000B, 0x000D, 0x0010, 0x002B, 0x0017, 0xff01, 0x0023 }));
            Assert.That(safariExtensionIds, Is.EqualTo(new ushort[] { 0x0000, 0x000A, 0x000B, 0x000D, 0x0010, 0x0017, 0x0023, 0xff01, 0x002B }));
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
}