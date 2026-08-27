using System.Linq;
using System.Reflection;
using System.Security.Authentication;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Profiles;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет установку транспорта и согласование прикладного протокола.
/// </summary>
/// <remarks>
/// Здесь закрепляются две разные вещи. Первая — работоспособность: предложить протокол, на котором
/// соединение говорить не умеет, значит получить его в ответе и развалить обмен на первом кадре.
/// Вторая — отпечаток: набор ALPN и политика legacy_session_id наблюдаемы сервером в первом же
/// пакете, и их расхождение с браузером не проявится ни одной ошибкой — только тем, что нас
/// начнут узнавать.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HttpsTransportConnectorTests
{
    private const int TestTimeoutMs = 30000;

    private static readonly ReadOnlyMemory<byte>[] Http2First = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11];

    private static readonly string[] Http2ThenHttp11 = ["h2", "http/1.1"];

    private static readonly string[] Http11Alone = ["http/1.1"];

    private static readonly ReadOnlyMemory<byte>[] ExoticProtocols = ["spdy/3.1"u8.ToArray()];

    private static readonly BrowserProfile[] AllProfiles =
    [
        BrowserProfileCatalog.CreateChromeDesktopWindowsTls13(),
        BrowserProfileCatalog.CreateChromeDesktopWindows(),
        BrowserProfileCatalog.CreateFirefoxDesktop(),
        BrowserProfileCatalog.CreateSafariDesktopMacOs(),
    ];

    [Test]
    public void IntersectAlpnKeepsProfileOrder()
    {
        var result = HttpsTransportConnector.IntersectAlpn(Http2First, HttpsTransportConnector.Http2AndHttp11);

        Assert.That(Decode(result), Is.EqualTo(Http2ThenHttp11).AsCollection);
    }

    [Test]
    public void IntersectAlpnDropsProtocolsTheConnectionCannotSpeak()
    {
        // Соединение HTTP/1.1 обязано сузить предложение профиля: иначе сервер выберет h2, а
        // разговор продолжится кадрами HTTP/1.1.
        var result = HttpsTransportConnector.IntersectAlpn(Http2First, HttpsTransportConnector.Http11Only);

        Assert.That(Decode(result), Is.EqualTo(Http11Alone).AsCollection);
    }

    [Test]
    public void IntersectAlpnFallsBackToAllowedWhenProfileSharesNothing()
    {
        // Пустое расширение ALPN хуже несовпадающего порядка: браузер отправляет его всегда.
        var result = HttpsTransportConnector.IntersectAlpn(ExoticProtocols, HttpsTransportConnector.Http11Only);

        Assert.That(Decode(result), Is.EqualTo(Http11Alone).AsCollection);
    }

    [Test]
    public void TlsSettingsOfferHttp2WhenConnectionAllowsIt()
    {
        var settings = InvokeCreateTlsSettings(CreateOptions(BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()), HttpsTransportConnector.Http2AndHttp11);

        Assert.That(Decode(GetAlpn(settings)), Is.EqualTo(Http2ThenHttp11).AsCollection);
    }

    [Test]
    public void TlsSettingsNarrowAlpnForHttp11OnlyConnections()
    {
        var settings = InvokeCreateTlsSettings(CreateOptions(BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()), HttpsTransportConnector.Http11Only);

        Assert.That(Decode(GetAlpn(settings)), Is.EqualTo(Http11Alone).AsCollection);
    }

    [Test]
    public void TlsSettingsSubstituteHostIntoEmptyServerName()
    {
        // Профиль объявляет расширение без имени: имя узла известно только на подключении.
        // Пустой SNI означал бы обращение «в никуда» — и на виртуальном хостинге другой сертификат.
        var settings = InvokeCreateTlsSettings(CreateOptions(BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()), HttpsTransportConnector.Http2AndHttp11);

        var serverName = settings.Extensions.OfType<ServerNameTlsExtension>().Single();

        Assert.That(serverName.HostName, Is.EqualTo("example.org"));
    }

    [Test]
    public void TlsSettingsKeepBrowserSessionIdPolicy()
    {
        // Пустой legacy_session_id — заметный признак не-браузерного клиента.
        var settings = InvokeCreateTlsSettings(CreateOptions(BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()), HttpsTransportConnector.Http2AndHttp11);

        Assert.That(settings.SessionIdPolicy, Is.EqualTo(SessionIdPolicy.Fixed32));
    }

    [Test]
    public void TlsSettingsDefaultToBrowserSessionIdPolicyWithoutProfile()
    {
        var settings = InvokeCreateTlsSettings(CreateOptions(profile: null), HttpsTransportConnector.Http11Only);

        Assert.That(settings.SessionIdPolicy, Is.EqualTo(SessionIdPolicy.Fixed32));
    }

    [Test]
    public void AllBrowserProfilesOfferHttp2InAlpn()
    {
        // Браузеры предлагают h2 независимо от версии TLS. Профиль, объявляющий только http/1.1,
        // отличим от браузера по одному этому расширению — и никогда не получит HTTP/2.
        foreach (var profile in AllProfiles)
        {
            var alpn = profile.Tls.Extensions.OfType<AlpnTlsExtension>().Single();

            Assert.That(Decode([.. alpn.Protocols]), Is.EqualTo(Http2ThenHttp11).AsCollection, profile.DisplayName);
        }
    }

    [Test]
    public void AllBrowserProfilesPreferHttp2()
    {
        foreach (var profile in AllProfiles)
            Assert.That(profile.PreferredHttpVersion, Is.EqualTo(System.Net.HttpVersion.Version20), profile.DisplayName);
    }

    [Test]
    public void AllBrowserProfilesDescribeHttp2Settings()
    {
        // Без настроек HTTP/2 профиль отдал бы соединению значения по умолчанию, и отпечаток
        // кадров перестал бы соответствовать заявленному браузеру.
        foreach (var profile in AllProfiles)
            Assert.That(profile.Http2, Is.Not.Null, profile.DisplayName);
    }

    [Test]
    public void ExplicitTls13RequestOverridesProfileCeiling()
    {
        var options = CreateOptions(BrowserProfileCatalog.CreateChromeDesktopWindows()) with { SslProtocols = SslProtocols.Tls13 };

        var settings = InvokeCreateTlsSettings(options, HttpsTransportConnector.Http2AndHttp11);

        Assert.That(settings.MaxVersion, Is.EqualTo(SslProtocols.Tls13));
    }

    private static HttpsConnectionOptions CreateOptions(BrowserProfile? profile)
        => new()
        {
            Host = "example.org",
            Port = 443,
            IsHttps = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ProfileTlsSettings = profile?.Tls,
            ProfileTcpSettings = profile?.Tcp,
        };

    private static IEnumerable<ReadOnlyMemory<byte>> GetAlpn(TlsSettings settings)
        => settings.Extensions.OfType<AlpnTlsExtension>().Single().Protocols;

    private static string[] Decode(IEnumerable<ReadOnlyMemory<byte>> protocols)
        => [.. protocols.Select(static protocol => System.Text.Encoding.ASCII.GetString(protocol.Span))];

    private static TlsSettings InvokeCreateTlsSettings(HttpsConnectionOptions options, IReadOnlyList<ReadOnlyMemory<byte>> alpn)
    {
        var method = typeof(HttpsTransportConnector).GetMethod("CreateTlsSettings", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateTlsSettings not found.");

        return (TlsSettings)(method.Invoke(obj: null, [options, alpn])
            ?? throw new InvalidOperationException("CreateTlsSettings invocation returned null."));
    }
}
