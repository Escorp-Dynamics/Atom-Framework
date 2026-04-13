using System.Net;
using System.Security.Authentication;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Каталог встроенных browser profiles.
/// </summary>
public static class BrowserProfileCatalog
{
    public static BrowserProfile CreateChromeDesktopLinux()
        => CreateChromiumProfile(
            "Chrome Desktop Linux",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    public static BrowserProfile CreateChromeDesktopWindows()
        => CreateChromiumProfile(
            "Chrome Desktop Windows",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    public static BrowserProfile CreateEdgeDesktopWindows()
        => CreateChromiumProfile(
            "Edge Desktop Windows",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

    public static BrowserProfile CreateFirefoxDesktop()
        => new()
        {
            DisplayName = "Firefox Desktop",
            UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0",
            PreferredHttpVersion = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateFirefoxTlsSettings(),
            Headers = CreateFirefoxHeaders(),
        };

    public static BrowserProfile CreateSafariDesktopMacOs()
        => new()
        {
            DisplayName = "Safari Desktop macOS",
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15",
            PreferredHttpVersion = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateSafariTlsSettings(),
            Headers = CreateSafariHeaders(),
        };

    public static BrowserProfile CreateChromiumProfile(string displayName, string userAgent)
        => new()
        {
            DisplayName = displayName,
            UserAgent = userAgent,
            PreferredHttpVersion = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateChromiumTlsSettings(),
            Headers = CreateChromiumHeaders(),
        };

    private static BrowserHeaderProfile CreateChromiumHeaders()
        => CreateHeaderProfile(useClientHints: true, GetChromiumDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateFirefoxHeaders()
        => CreateHeaderProfile(useClientHints: false, GetFirefoxDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateSafariHeaders()
        => CreateHeaderProfile(useClientHints: false, GetSafariDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateHeaderProfile(bool useClientHints, ReferrerPolicyMode defaultReferrerPolicy)
        => new()
        {
            DefaultRequestKind = RequestKind.Fetch,
            DefaultReferrerPolicy = defaultReferrerPolicy,
            UseOriginalHeaderCase = true,
            UsePreserveHeaderOrder = true,
            UseConnectionKeepAlive = true,
            UseClientHints = useClientHints,
            EmitAcceptEncoding = true,
            EmitAcceptLanguage = true,
        };

    private static ReferrerPolicyMode GetChromiumDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static ReferrerPolicyMode GetFirefoxDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static ReferrerPolicyMode GetSafariDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static TcpSettings CreateTcpSettings()
        => new()
        {
            IsNagleDisabled = true,
            UseHappyEyeballsAlternating = true,
            AttemptTimeout = TimeSpan.FromSeconds(3),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

    private static TlsSettings CreateChromiumTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites =
            [
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
            ],
            Extensions = CreateChromiumExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
        };

    private static TlsSettings CreateFirefoxTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites =
            [
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            ],
            Extensions = CreateFirefoxExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
        };

    private static TlsSettings CreateSafariTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites =
            [
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            ],
            Extensions = CreateSafariExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
        };

    private static ITlsExtension[] CreateChromiumExtensions()
        =>
        [
            new ServerNameTlsExtension(),
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            CreateSupportedGroupsExtension(
                NamedGroup.X25519,
                NamedGroup.Secp256r1,
                NamedGroup.Secp384r1),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            new SessionTicketExtension(),
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPkcs1Sha1),
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
        ];

    private static ITlsExtension[] CreateFirefoxExtensions()
        =>
        [
            new ServerNameTlsExtension(),
            CreateSupportedGroupsExtension(
                NamedGroup.X25519,
                NamedGroup.Secp256r1,
                NamedGroup.Secp384r1,
                NamedGroup.Secp521r1),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.EcdsaSecp521r1Sha512,
                SignatureAlgorithm.Ed25519,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPkcs1Sha512),
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            new SessionTicketExtension(),
        ];

    private static ITlsExtension[] CreateSafariExtensions()
        =>
        [
            new ServerNameTlsExtension(),
            CreateSupportedGroupsExtension(
                NamedGroup.Secp256r1,
                NamedGroup.Secp384r1,
                NamedGroup.X25519),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.RsaPkcs1Sha384),
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new SessionTicketExtension(),
            new RenegotiationInfoTlsExtension(),
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
        ];

    private static SignatureAlgorithmsTlsExtension CreateSignatureAlgorithmsExtension(params SignatureAlgorithm[] algorithms)
        => new()
        {
            Algorithms = algorithms,
        };

    private static SupportedGroupsTlsExtension CreateSupportedGroupsExtension(params NamedGroup[] groups)
        => new()
        {
            Groups = groups,
        };
}