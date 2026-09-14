using System.Reflection;
using Atom.Net.Https;
using Atom.Net.Https.Profiles;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверки переотправки навигационного прокси под профилем задачи: однозначность личности
/// в реестре маршрутов, снятие профильных заголовков с копии клиента, пометка навигации
/// и разделение пулов переотправки по личности.
/// </summary>
[TestFixture]
public sealed class WebDriverNavigationProxyForwardProfileTests
{
    private static readonly BrowserProfile ChromeProfile = BrowserProfileResolver.Resolve(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    private static readonly BrowserProfile FirefoxProfile = BrowserProfileResolver.Resolve(
        "Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0");

    // Профили каталога берутся как есть: реестр различает личности по строке агента и не трогает
    // BrowserProfile.Equals, который на любом каталожном профиле падает NullReferenceException
    // (Http2Settings.Equals → TlsSettings.Equals → CipherSuites.Equals на неинициализированном поле).
    private static readonly BrowserProfile ComparableChromeProfile = ChromeProfile;

    private static readonly BrowserProfile ComparableFirefoxProfile = FirefoxProfile;

    [Test]
    public void HasUnambiguousForwardProfileIsTrueForEmptyRegistry()
    {
        var registry = new ProxyNavigationDecisionRegistry();

        Assert.That(registry.HasUnambiguousForwardProfile(), Is.True);
    }

    [Test]
    public void HasUnambiguousForwardProfileIsTrueForSingleRoute()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("context-1", "token-1", ComparableChromeProfile));

        Assert.That(registry.HasUnambiguousForwardProfile(), Is.True);
    }

    [Test]
    public void HasUnambiguousForwardProfileIsTrueWhenAllRoutesShareProfile()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("context-1", "token-1", ComparableChromeProfile));
        registry.UpsertRoute(CreateRoute("context-2", "token-2", ComparableChromeProfile));

        Assert.That(registry.HasUnambiguousForwardProfile(), Is.True);
    }

    [Test]
    public void HasUnambiguousForwardProfileIsFalseWhenRoutesCarryDifferentProfiles()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("context-1", "token-1", ComparableChromeProfile));
        registry.UpsertRoute(CreateRoute("context-2", "token-2", ComparableFirefoxProfile));

        Assert.That(registry.HasUnambiguousForwardProfile(), Is.False);
    }

    [Test]
    public void HasUnambiguousForwardProfileIsFalseWhenOnlyOneRouteCarriesProfile()
    {
        var registry = new ProxyNavigationDecisionRegistry();
        registry.UpsertRoute(CreateRoute("context-1", "token-1", ComparableChromeProfile));
        registry.UpsertRoute(CreateRoute("context-2", "token-2", forwardProfile: null));

        Assert.That(registry.HasUnambiguousForwardProfile(), Is.False);
    }

    [Test]
    public async Task CreateForwardRequestDropsProfileOwnedHeadersWhenProfileIsInEffect()
    {
        await using var server = CreateProxyServer();

        using var forwardRequest = InvokeCreateForwardRequest(
            server,
            CreateClientHeaders(fetchDestination: "document"),
            ChromeProfile);

        Assert.Multiple(() =>
        {
            Assert.That(forwardRequest.Headers.Contains("User-Agent"), Is.False, "Профиль сам проставляет агент — копия клиента не должна уезжать наружу.");
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-platform"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-mobile"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-full-version-list"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-platform-version"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-arch"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-model"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-bitness"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-full-version"), Is.False);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-wow64"), Is.False);

            // Заголовки вне ведения профиля копируются как прежде.
            Assert.That(forwardRequest.Headers.Contains("Accept-Language"), Is.True);
            Assert.That(forwardRequest.Headers.Contains("Sec-Fetch-Dest"), Is.True);
        });
    }

    [Test]
    public async Task CreateForwardRequestKeepsClientHeadersWhenProfileIsAbsent()
    {
        await using var server = CreateProxyServer();

        using var forwardRequest = InvokeCreateForwardRequest(
            server,
            CreateClientHeaders(fetchDestination: "document"),
            forwardProfileInEffect: null);

        Assert.Multiple(() =>
        {
            Assert.That(forwardRequest.Headers.Contains("User-Agent"), Is.True);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua"), Is.True);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-platform"), Is.True);
            Assert.That(forwardRequest.Headers.Contains("sec-ch-ua-mobile"), Is.True);
            Assert.That(forwardRequest.Headers.Contains("Accept-Language"), Is.True);
        });
    }

    [Test]
    public async Task CreateForwardRequestMarksNavigationRequestKind()
    {
        await using var server = CreateProxyServer();

        using var forwardRequest = InvokeCreateForwardRequest(
            server,
            CreateClientHeaders(fetchDestination: "document"),
            ChromeProfile);

        Assert.That(TryReadRequestKind(forwardRequest), Is.EqualTo(RequestKind.Navigation));
    }

    [Test]
    public async Task CreateForwardRequestLeavesSubresourceRequestKindUnset()
    {
        await using var server = CreateProxyServer();

        using var forwardRequest = InvokeCreateForwardRequest(
            server,
            CreateClientHeaders(fetchDestination: "image"),
            ChromeProfile);

        Assert.That(TryReadRequestKind(forwardRequest), Is.Null);
    }

    [Test]
    public async Task GetForwardClientSeparatesPoolsByForwardProfile()
    {
        await using var server = CreateProxyServer();

        var chromeClient = InvokeGetForwardClient(server, "http://127.0.0.1:8181", "token-1", ChromeProfile);
        var firefoxClient = InvokeGetForwardClient(server, "http://127.0.0.1:8181", "token-1", FirefoxProfile);
        var profilelessClient = InvokeGetForwardClient(server, "http://127.0.0.1:8181", "token-1", routeForwardProfile: null);

        Assert.Multiple(() =>
        {
            Assert.That(firefoxClient, Is.Not.SameAs(chromeClient), "Разные личности обязаны получать разные пулы соединений.");
            Assert.That(profilelessClient, Is.Not.SameAs(chromeClient), "Запрос без профиля не должен переиспользовать клиент профильной задачи.");
            Assert.That(profilelessClient, Is.Not.SameAs(firefoxClient));
        });
    }

    [Test]
    public async Task GetForwardClientReusesPoolForSameRouteAndProfile()
    {
        await using var server = CreateProxyServer();

        var first = InvokeGetForwardClient(server, "http://127.0.0.1:8181", "token-1", ChromeProfile);
        var second = InvokeGetForwardClient(server, "http://127.0.0.1:8181", "token-1", ChromeProfile);

        Assert.That(second, Is.SameAs(first));
    }

    private static BridgeNavigationProxyServer CreateProxyServer()
        => new("127.0.0.1", 0, static () => null);

    private static ProxyNavigationRoute CreateRoute(string contextId, string routeToken, BrowserProfile? forwardProfile)
        => new()
        {
            SessionId = "session-1",
            TabId = "tab-1",
            ContextId = contextId,
            RouteToken = routeToken,
            UpstreamProxy = "http://127.0.0.1:8181",
            ForwardProfile = forwardProfile,
            Revision = 1,
        };

    private static Dictionary<string, string> CreateClientHeaders(string fetchDestination)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = "example.test",
            ["User-Agent"] = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ["sec-ch-ua"] = "\"Chromium\";v=\"120\", \"Not=A?Brand\";v=\"8\"",
            ["sec-ch-ua-full-version-list"] = "\"Chromium\";v=\"120.0.6099.109\"",
            ["sec-ch-ua-platform"] = "\"Linux\"",
            ["sec-ch-ua-platform-version"] = "\"6.5.0\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-arch"] = "\"x86\"",
            ["sec-ch-ua-model"] = "\"\"",
            ["sec-ch-ua-bitness"] = "\"64\"",
            ["sec-ch-ua-full-version"] = "\"120.0.6099.109\"",
            ["sec-ch-ua-wow64"] = "?0",
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Sec-Fetch-Dest"] = fetchDestination,
        };

    /// <summary>
    /// Вызывает приватный <c lang="text">CreateForwardRequest</c>: собрать его аргумент
    /// <c lang="text">ProxyRequest</c> снаружи иначе нельзя — тип вложенный и приватный.
    /// </summary>
    private static HttpRequestMessage InvokeCreateForwardRequest(
        BridgeNavigationProxyServer server,
        Dictionary<string, string> clientHeaders,
        BrowserProfile? forwardProfileInEffect)
    {
        const string forwardTargetUrl = "https://example.test/page";

        var proxyRequestType = typeof(BridgeNavigationProxyServer).GetNestedType("ProxyRequest", BindingFlags.NonPublic);
        Assert.That(proxyRequestType, Is.Not.Null, "Не найден вложенный тип ProxyRequest.");

        var clientRequest = Activator.CreateInstance(
            proxyRequestType!,
            ["GET", forwardTargetUrl, clientHeaders, null, 0, Array.Empty<byte>()]);

        var issuedAtUtc = DateTimeOffset.UtcNow;
        var decision = new ProxyNavigationPendingDecision
        {
            RequestId = "request-1",
            Method = "GET",
            AbsoluteUrl = forwardTargetUrl,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = issuedAtUtc.AddSeconds(5),
            Action = ProxyNavigationDecisionAction.Continue,
        };

        var method = typeof(BridgeNavigationProxyServer).GetMethod(
            "CreateForwardRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Не найден CreateForwardRequest.");

        return (HttpRequestMessage)method!.Invoke(server, [clientRequest, decision, forwardTargetUrl, forwardProfileInEffect])!;
    }

    private static HttpClient InvokeGetForwardClient(
        BridgeNavigationProxyServer server,
        string? upstreamProxy,
        string routeToken,
        BrowserProfile? routeForwardProfile)
    {
        var method = typeof(BridgeNavigationProxyServer).GetMethod(
            "GetForwardClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Не найден GetForwardClient.");

        return (HttpClient)method!.Invoke(server, [upstreamProxy, routeToken, routeForwardProfile])!;
    }

    /// <summary>
    /// Читает тип browser-shaped запроса: публичного геттера у опции нет.
    /// </summary>
    private static RequestKind? TryReadRequestKind(HttpRequestMessage request)
    {
        var method = typeof(HttpsRequestOptions).GetMethod(
            "TryGetRequestKind",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Не найден HttpsRequestOptions.TryGetRequestKind.");

        object?[] arguments = [request, null];
        return method!.Invoke(obj: null, arguments) is true ? (RequestKind)arguments[1]! : null;
    }
}
