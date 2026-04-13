using System.Net;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverBridgeTabContextProxyTests
{
    [Test]
    public async Task BuildSetTabContextPayloadSerializesPageProxyCredentials()
    {
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: null, bridgeBootstrap: null);
        var page = (WebPage)await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
        {
            Proxy = new WebProxy("http://proxy.example.com:8080")
            {
                Credentials = new NetworkCredential("user", "pass"),
            },
        }).ConfigureAwait(false);

        var payload = WebBrowser.BuildSetTabContextPayload(page);

        Assert.Multiple(() =>
        {
            Assert.That(payload["proxy"]?.GetValue<string>(), Is.EqualTo("http://user:pass@proxy.example.com:8080/"));
            Assert.That(payload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("webrequest"));
            Assert.That(payload.ContainsKey("navigationProxyRouteToken"), Is.False);
        });
    }

    [Test]
    public async Task BuildSetTabContextPayloadPreservesExplicitDirectProxyMode()
    {
        await using var browser = new WebBrowser(new WebBrowserSettings
        {
            Proxy = new WebProxy("http://browser-proxy.example.com:8080"),
        }, materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: null, bridgeBootstrap: null);
        var window = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            UseProxy = false,
        }).ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;

        var payload = WebBrowser.BuildSetTabContextPayload(page);

        Assert.Multiple(() =>
        {
            Assert.That(payload.ContainsKey("proxy"), Is.True);
            Assert.That(payload["proxy"], Is.Null);
            Assert.That(payload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("webrequest"));
            Assert.That(payload.ContainsKey("navigationProxyRouteToken"), Is.False);
        });
    }

    [Test]
    public async Task BuildSetTabContextPayloadUsesProxyModeWhenProxyNavigationRouteExists()
    {
        await using var browser = new WebBrowser(new WebBrowserSettings(), materializedProfilePath: null, browserProcess: null, display: null, ownsDisplay: false, bridgeServer: null, bridgeBootstrap: null);
        var page = (WebPage)browser.CurrentPage;

        browser.ProxyNavigationDecisions.UpsertRoute(new ProxyNavigationRoute
        {
            SessionId = "session-1",
            TabId = page.TabId,
            ContextId = page.GetOrCreateBridgeContextId(),
            RouteToken = "proxy-token-1",
            Revision = 1,
        });

        var payload = WebBrowser.BuildSetTabContextPayload(page);

        Assert.Multiple(() =>
        {
            Assert.That(payload["navigationInterceptionMode"]?.GetValue<string>(), Is.EqualTo("proxy"));
            Assert.That(payload["navigationProxyRouteToken"]?.GetValue<string>(), Is.EqualTo("proxy-token-1"));
        });
    }
}